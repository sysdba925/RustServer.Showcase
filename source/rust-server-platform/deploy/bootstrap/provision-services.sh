#!/usr/bin/env bash
# Prepare Production dependencies for Control, Versions, Plugins and Snapshots.
# Updater remains the only application release installer and activator.
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly SCRIPT_DIR
PLATFORM_DIR="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"
readonly PLATFORM_DIR
# shellcheck source=lib/common.sh
source "$SCRIPT_DIR/lib/common.sh"
# shellcheck source=lib/rcon-secret-permissions.sh
source "$SCRIPT_DIR/lib/rcon-secret-permissions.sh"
# shellcheck source=../../scripts/lib/rust-server-configuration.sh
source "$PLATFORM_DIR/scripts/lib/rust-server-configuration.sh"

readonly SERVER_ENV_FILE="/etc/rustserver/rust-server.env"
readonly RUNTIME_ASSETS_INSTALLER="$PLATFORM_DIR/scripts/bootstrap.d/82-install-runtime-assets.sh"
readonly SYSTEMD_SOURCE_DIR="$PLATFORM_DIR/deploy/systemd"
readonly SYSTEMD_TARGET_DIR="/etc/systemd/system"
readonly CONTROL_USER='rustserver-control'
readonly CONTROL_GROUP='rustserver-control'
readonly CONTROL_DIRECTORY='/opt/rustserver/control'
readonly CONTROL_RELEASES_DIRECTORY="$CONTROL_DIRECTORY/releases"
readonly CONTROL_CURRENT_LINK="$CONTROL_DIRECTORY/current"
readonly NFTABLES_PACKAGE='nftables'
readonly IPROUTE2_PACKAGE='iproute2'
readonly KMOD_PACKAGE='kmod'
readonly NF_TABLES_MODULE='nf_tables'
readonly NF_TABLES_MODULES_LOAD_FILE='/etc/modules-load.d/rustserver-network-access.conf'
readonly SERVICE_NAMES=(control versions plugins snapshots)
readonly SERVICE_USERS=(rustserver-control rustserver-versions rustserver-plugins rustserver-snapshots)

ensure_packages() {
  local -a packages=()
  if ! command -v docker >/dev/null 2>&1 || ! docker compose version >/dev/null 2>&1; then
    if dpkg-query -W -f='${db:Status-Status}' docker-ce 2>/dev/null | grep -qx installed; then
      packages+=(docker-compose-plugin)
    else
      packages+=(docker.io docker-compose-v2)
    fi
  fi
  if ! command -v git >/dev/null 2>&1; then
    packages+=(git)
  fi
  if ! command -v setfacl >/dev/null 2>&1; then
    packages+=(acl)
  fi
  if ! command -v nft >/dev/null 2>&1; then
    packages+=("$NFTABLES_PACKAGE")
  fi
  if ! command -v ss >/dev/null 2>&1; then
    packages+=("$IPROUTE2_PACKAGE")
  fi
  if ! command -v modprobe >/dev/null 2>&1; then
    packages+=("$KMOD_PACKAGE")
  fi
  if (( ${#packages[@]} )); then
    run_as_root apt-get update
    run_as_root env DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends "${packages[@]}" ||
      bootstrap_die "Could not install production dependencies: ${packages[*]}"
  fi
  require_command docker
  require_command git
  require_command modprobe
  require_command nft
  require_command setfacl
  require_command ss
  docker compose version >/dev/null || bootstrap_die "Docker Compose v2 is unavailable"
  run_as_root systemctl enable --now docker.service ||
    bootstrap_die "Docker Engine could not be started"
  run_as_root systemctl is-active --quiet docker.service ||
    bootstrap_die "Docker Engine is not active"
}

configure_nf_tables_module_loading() {
  local temporary_file

  run_as_root install -d -m 0755 -o root -g root -- /etc/modules-load.d ||
    bootstrap_die 'Could not create the kernel module load configuration directory'
  if root_path_exists "$NF_TABLES_MODULES_LOAD_FILE"; then
    require_root_regular_file "$NF_TABLES_MODULES_LOAD_FILE"
    if ! read_root_regular_file "$NF_TABLES_MODULES_LOAD_FILE" | grep -Fxq "$NF_TABLES_MODULE"; then
      bootstrap_die "NetworkAccess module-load configuration must enable $NF_TABLES_MODULE: $NF_TABLES_MODULES_LOAD_FILE"
    fi
    bootstrap_skip "Preserving NetworkAccess module-load configuration: $NF_TABLES_MODULES_LOAD_FILE"
  else
    temporary_file="$(run_as_root mktemp --tmpdir=/etc/modules-load.d '.rustserver-network-access.conf.tmp.XXXXXX')" || \
      bootstrap_die 'Could not stage the NetworkAccess module-load configuration'
    if ! printf '%s\n' \
      '# Required by RustServer.Control NetworkAccess.' \
      "$NF_TABLES_MODULE" | run_as_root tee "$temporary_file" >/dev/null; then
      run_as_root rm -f -- "$temporary_file"
      bootstrap_die 'Could not write the NetworkAccess module-load configuration'
    fi
    if ! run_as_root install -m 0644 -o root -g root -- "$temporary_file" "$NF_TABLES_MODULES_LOAD_FILE"; then
      run_as_root rm -f -- "$temporary_file"
      bootstrap_die 'Could not install the NetworkAccess module-load configuration'
    fi
    run_as_root rm -f -- "$temporary_file"
    bootstrap_info "Configured $NF_TABLES_MODULE to load at boot"
  fi

  run_as_root modprobe "$NF_TABLES_MODULE" ||
    bootstrap_die "Could not load required kernel module: $NF_TABLES_MODULE"
  run_as_root nft list tables >/dev/null ||
    bootstrap_die 'nftables is unavailable after loading nf_tables'
}

provision_server_environment() {
  local temporary_file
  local template="$PLATFORM_DIR/docker/rust-server/.env.example"
  require_regular_file "$template"
  ensure_root_directory /etc/rustserver 0755 root root
  if root_path_exists "$SERVER_ENV_FILE"; then
    require_root_regular_file "$SERVER_ENV_FILE"
    bootstrap_skip "Preserving existing Rust server environment: $SERVER_ENV_FILE"
  else
    temporary_file="$(run_as_root mktemp /etc/rustserver/.rust-server.env.tmp.XXXXXX)" ||
      bootstrap_die "Could not allocate temporary Rust server configuration"
    run_as_root install -o root -g root -m 0644 -- "$template" "$temporary_file" || {
      run_as_root rm -f -- "$temporary_file"
      bootstrap_die "Could not stage Rust server configuration"
    }
    if ! run_as_root ln -- "$temporary_file" "$SERVER_ENV_FILE"; then
      run_as_root rm -f -- "$temporary_file"
      require_root_regular_file "$SERVER_ENV_FILE"
      bootstrap_skip "Preserving concurrently created Rust server environment"
    else
      run_as_root rm -f -- "$temporary_file"
      bootstrap_info "Created Rust server environment from existing template"
    fi
  fi
  rust_server_validate_configuration "$SERVER_ENV_FILE"
}

ensure_service_user() {
  local user="$1"
  local state_path="$2"
  if getent passwd "$user" >/dev/null; then
    bootstrap_skip "Preserving service account: $user"
  else
    run_as_root useradd --system --user-group --home-dir "$state_path" \
      --shell /usr/sbin/nologin "$user" ||
      bootstrap_die "Could not create service account: $user"
  fi
}

verify_control_release_boundary() {
  local path="$1"
  local owner
  local mode
  local insecure_path

  owner="$(run_as_root stat -c '%U' -- "$path")" ||
    bootstrap_die "Could not inspect Control release path ownership: $path"
  mode="$(run_as_root stat -c '%a' -- "$path")" ||
    bootstrap_die "Could not inspect Control release path permissions: $path"
  [[ "$owner" == root && "$mode" =~ ^[0-7]{3,4}$ && $((8#$mode & 022)) == 0 ]] ||
    bootstrap_die "Control release path must be root-owned and not group/other writable: $path"

  insecure_path="$(run_as_root find "$path" -xdev \( -type f -o -type d \) \( ! -user root -o -perm /022 \) -print -quit)" ||
    bootstrap_die "Could not inspect Control release tree permissions: $path"
  [[ -z "$insecure_path" ]] ||
    bootstrap_die "Control release tree must remain root-owned and not group/other writable: $insecure_path"
}

provision_control_release_boundary() {
  local current_target

  ensure_root_directory "$CONTROL_DIRECTORY" 0755 root root
  ensure_root_directory "$CONTROL_RELEASES_DIRECTORY" 0755 root root
  verify_control_release_boundary "$CONTROL_DIRECTORY"
  verify_control_release_boundary "$CONTROL_RELEASES_DIRECTORY"

  if ! root_path_exists "$CONTROL_CURRENT_LINK"; then
    return
  fi

  run_as_root test -L "$CONTROL_CURRENT_LINK" ||
    bootstrap_die "Control current path must be a symbolic link: $CONTROL_CURRENT_LINK"
  current_target="$(run_as_root readlink -e -- "$CONTROL_CURRENT_LINK")" ||
    bootstrap_die "Control current symbolic link is dangling: $CONTROL_CURRENT_LINK"
  [[ "$current_target" == "$CONTROL_RELEASES_DIRECTORY/"* && -d "$current_target" ]] ||
    bootstrap_die "Control current symbolic link points outside immutable releases: $CONTROL_CURRENT_LINK"
  verify_control_release_boundary "$current_target"
}

ensure_game_directory() {
  local directory="$1"
  # cm2network/steamcmd:latest uses steam UID 1000. Grant access to that UID
  # and file-managing services without rewriting existing game files.
  if root_path_exists "$directory"; then
    require_directory "$directory"
  else
    run_as_root install -d -o 1000 -g rustserver-game -m 2775 -- "$directory" ||
      bootstrap_die "Could not create Rust data directory: $directory"
  fi
  run_as_root setfacl -m 'u:1000:rwx,g:rustserver-game:rwx,d:u:1000:rwx,d:g:rustserver-game:rwx' -- "$directory" ||
    bootstrap_die "Could not grant Rust data access: $directory"
}

provision_service_state() {
  local service user
  local game_root identity
  game_root="$(rust_server_env_read_value "$SERVER_ENV_FILE" RUST_SERVER_PATH)" ||
    bootstrap_die "RUST_SERVER_PATH is missing from $SERVER_ENV_FILE"
  identity="$(rust_server_env_read_value "$SERVER_ENV_FILE" RUST_SERVER_IDENTITY)" ||
    bootstrap_die "RUST_SERVER_IDENTITY is missing from $SERVER_ENV_FILE"
  [[ "$game_root" == /* && "$game_root" != "/" && "$game_root" != "/srv" && "$game_root" != "/srv/rust" ]] ||
    bootstrap_die "Unsafe RUST_SERVER_PATH in $SERVER_ENV_FILE"
  [[ "$identity" =~ ^[A-Za-z0-9_-]+$ ]] ||
    bootstrap_die "Invalid RUST_SERVER_IDENTITY in $SERVER_ENV_FILE"

  if ! getent group rustserver-game >/dev/null; then
    run_as_root groupadd --system rustserver-game
  fi
  for service in "${SERVICE_NAMES[@]}"; do
    ensure_root_directory "/opt/rustserver/$service" 0755 root root
    ensure_root_directory "/opt/rustserver/$service/releases" 0755 root root
  done
  ensure_root_directory /var/lib/rustserver 0755 root root
  for user in "${SERVICE_USERS[@]}"; do
    ensure_service_user "$user" "/var/lib/rustserver/${user#rustserver-}"
  done
  getent group "$CONTROL_GROUP" >/dev/null ||
    bootstrap_die "Control helper group is missing: $CONTROL_GROUP"
  run_as_root usermod -aG docker "$CONTROL_USER"
  run_as_root usermod -aG rustserver-game rustserver-plugins
  run_as_root usermod -aG rustserver-game rustserver-snapshots
  ensure_root_directory /var/lib/rustserver/control 0700 rustserver-control rustserver-control
  ensure_root_directory /var/lib/rustserver/plugins 0750 rustserver-plugins rustserver-plugins
  ensure_root_directory /var/lib/rustserver/plugins/workspaces 0750 rustserver-plugins rustserver-plugins
  ensure_root_directory /var/lib/rustserver/plugins/workspaces/plugins 0750 rustserver-plugins rustserver-plugins
  ensure_root_directory /var/lib/rustserver/plugins/workspaces/configs 0750 rustserver-plugins rustserver-plugins
  ensure_root_directory /var/lib/rustserver/snapshots 0750 rustserver-snapshots rustserver-snapshots

  if [[ "$game_root" == "/srv/rust/server" ]]; then
    ensure_root_directory /srv/rust 0755 root root
  else
    require_directory "$(dirname -- "$game_root")"
  fi
  ensure_game_directory "$game_root"
  ensure_game_directory "$game_root/server"
  ensure_game_directory "$game_root/server/$identity"
  ensure_game_directory "$game_root/oxide"
  for service in data config plugins lang; do
    ensure_game_directory "$game_root/oxide/$service"
  done

  provision_control_release_boundary
}

readonly PLUGINS_GITHUB_TOKEN_SOURCE='/etc/rustserver/plugins-github-token'
readonly PLUGINS_CREDENTIAL_DROPIN_DIRECTORY='/etc/systemd/system/rustserver-plugins.service.d'
readonly PLUGINS_CREDENTIAL_DROPIN_PATH="$PLUGINS_CREDENTIAL_DROPIN_DIRECTORY/credentials.conf"

validate_existing_plugins_token() {
  local owner group mode
  require_root_regular_file "$PLUGINS_GITHUB_TOKEN_SOURCE"
  owner="$(run_as_root stat -c '%U' -- "$PLUGINS_GITHUB_TOKEN_SOURCE")"
  group="$(run_as_root stat -c '%G' -- "$PLUGINS_GITHUB_TOKEN_SOURCE")"
  mode="$(run_as_root stat -c '%a' -- "$PLUGINS_GITHUB_TOKEN_SOURCE")"
  [[ "$owner" == root && "$group" == root && "$mode" == 600 ]] ||
    bootstrap_die 'Plugins GitHub token must be owned by root:root with mode 0600'
  [[ "$(read_root_regular_file "$PLUGINS_GITHUB_TOKEN_SOURCE")" =~ ^[A-Za-z0-9_=-]+$ ]] ||
    bootstrap_die 'Plugins GitHub credential contains invalid characters'
}

provision_plugins_github_token() {
  local source_file temporary_file
  if root_path_exists "$PLUGINS_GITHUB_TOKEN_SOURCE"; then
    validate_existing_plugins_token
    bootstrap_skip 'Preserving existing Plugins GitHub credential'
    return
  fi

  source_file="${RUSTSERVER_BOOTSTRAP_PLUGINS_GITHUB_TOKEN_FILE:-}"
  [[ -n "$source_file" ]] ||
    bootstrap_die 'A dedicated Plugins GitHub token is required on first install; rerun Installer with PLUGINS_GITHUB_TOKEN or PLUGINS_GITHUB_TOKEN_FILE'
  require_private_token_file "$source_file"
  [[ "$(run_as_root cat -- "$source_file")" =~ ^[A-Za-z0-9_=-]+$ ]] ||
    bootstrap_die 'Plugins GitHub token contains invalid characters'

  temporary_file="$(run_as_root mktemp /etc/rustserver/.plugins-github-token.tmp.XXXXXX)" ||
    bootstrap_die 'Could not stage the Plugins credential'
  if ! run_as_root install -o root -g root -m 0600 -- "$source_file" "$temporary_file"; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not securely stage the Plugins credential'
  fi
  if ! run_as_root ln -- "$temporary_file" "$PLUGINS_GITHUB_TOKEN_SOURCE"; then
    run_as_root rm -f -- "$temporary_file"
    validate_existing_plugins_token
    bootstrap_skip 'Preserving concurrently created Plugins credential'
    return
  fi
  run_as_root rm -f -- "$temporary_file"
  bootstrap_info 'Created independent Plugins GitHub credential'
}

write_plugins_credential_dropin() {
  local temporary_file
  run_as_root install -d -o root -g root -m 0755 -- "$PLUGINS_CREDENTIAL_DROPIN_DIRECTORY" ||
    bootstrap_die 'Could not create Plugins credential drop-in directory'
  temporary_file="$(run_as_root mktemp "$PLUGINS_CREDENTIAL_DROPIN_DIRECTORY/.credentials.conf.tmp.XXXXXX")" ||
    bootstrap_die 'Could not stage Plugins credential drop-in'
  if ! printf '%s\n' \
    '[Service]' \
    'LoadCredential=github-token:/etc/rustserver/plugins-github-token' \
    'Environment=RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE=%d/github-token' \
    'Environment=PluginManagement__WorkspaceInitialization__Enabled=true' \
    'InaccessiblePaths=/etc/rustserver/plugins-github-token' |
    run_as_root tee "$temporary_file" >/dev/null; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not write Plugins credential drop-in'
  fi
  run_as_root chmod 0644 -- "$temporary_file"
  if run_as_root test -f "$PLUGINS_CREDENTIAL_DROPIN_PATH" &&
     run_as_root cmp -s -- "$temporary_file" "$PLUGINS_CREDENTIAL_DROPIN_PATH"; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_skip 'Plugins credential drop-in is already current'
    return
  fi
  run_as_root mv -Tf -- "$temporary_file" "$PLUGINS_CREDENTIAL_DROPIN_PATH" ||
    bootstrap_die 'Could not install Plugins credential drop-in'
  bootstrap_info 'Configured Plugins systemd credential'
}

install_reviewed_unit() {
  local name="$1"
  local source="$SYSTEMD_SOURCE_DIR/rustserver-$name.service"
  local target="$SYSTEMD_TARGET_DIR/rustserver-$name.service"
  local temporary_file
  require_regular_file "$source"
  if root_path_exists "$target"; then
    require_root_regular_file "$target"
    run_as_root cmp -s -- "$source" "$target" ||
      bootstrap_die "Existing systemd unit differs from reviewed Production file: $target"
    bootstrap_skip "Production systemd unit already installed: $name"
    return
  fi
  temporary_file="$(run_as_root mktemp "$SYSTEMD_TARGET_DIR/.rustserver-$name.tmp.XXXXXX")" ||
    bootstrap_die "Could not allocate temporary systemd unit for $name"
  run_as_root install -o root -g root -m 0644 -- "$source" "$temporary_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die "Could not stage systemd unit for $name"
  }
  if ! run_as_root ln -- "$temporary_file" "$target"; then
    run_as_root rm -f -- "$temporary_file"
    require_root_regular_file "$target"
    run_as_root cmp -s -- "$source" "$target" ||
      bootstrap_die "Conflicting systemd unit appeared while provisioning: $target"
  else
    run_as_root rm -f -- "$temporary_file"
    bootstrap_info "Installed production systemd unit: $name"
  fi
}

main() {
  bootstrap_mode_is install || bootstrap_die "Unsupported provisioning mode: $(bootstrap_mode)"
  ensure_sudo_access
  for required in cmp dpkg-query find getent groupadd grep useradd usermod systemctl install readlink stat; do
    require_command "$required"
  done
  ensure_packages
  configure_nf_tables_module_loading
  provision_server_environment
  require_root_regular_file /etc/rustserver/topology.env
  require_root_regular_file /etc/rustserver/rcon-password
  bash "$RUNTIME_ASSETS_INSTALLER"
  provision_service_state
  rcon_secret_grant_game_container_read /etc/rustserver/rcon-password
  local service
  for service in "${SERVICE_NAMES[@]}"; do
    install_reviewed_unit "$service"
  done
  provision_plugins_github_token
  write_plugins_credential_dropin
  run_as_root systemctl daemon-reload ||
    bootstrap_die "systemd daemon-reload failed"
  bootstrap_info "Four services provisioned; release activation belongs to Updater"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
