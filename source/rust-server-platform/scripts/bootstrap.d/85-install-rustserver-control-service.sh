#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly CONTROL_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Control"
readonly CONTROL_API_PROJECT_DIR="$CONTROL_REPOSITORY_DIR/src/RustServer.Control.Api"
readonly CONTROL_RELEASE_DIR="$CONTROL_API_PROJECT_DIR/bin/Release/net10.0"
readonly CONTROL_DLL="$CONTROL_RELEASE_DIR/RustServer.Control.Api.dll"
readonly RUNTIME_DIRECTORY="/opt/rustserver/runtime"
readonly RUST_SERVER_COMPOSE_FILE="$RUNTIME_DIRECTORY/rust-server/compose.yml"
readonly RUST_SERVER_ENV_FILE="/etc/rustserver/rust-server.env"
readonly CONTROL_STATE_DIRECTORY="/var/lib/rustserver/control"
readonly CONTROL_CONFIGURATION_FILE="$CONTROL_STATE_DIRECTORY/server-configuration.json"
readonly LEGACY_CONTROL_CONFIGURATION_FILE="$PLATFORM_DIR/docker/rust-server/server-configuration.json"
readonly STEAMCMD_COMPOSE_FILE="$RUNTIME_DIRECTORY/steamcmd/compose.yml"
readonly OXIDE_COMPOSE_FILE="$RUNTIME_DIRECTORY/oxide/compose.yml"
readonly SERVICE_NAME="rustserver-control.service"
readonly SERVICE_UNIT_PATH="/etc/systemd/system/$SERVICE_NAME"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"
# shellcheck source=../lib/rust-server-configuration.sh
source "$STEP_DIR/../lib/rust-server-configuration.sh"
# shellcheck source=../lib/topology.sh
source "$STEP_DIR/../lib/topology.sh"

run_as_root() {
  if (( EUID == 0 )); then
    command "$@"
  else
    sudo "$@"
  fi
}

run_as_service_user() {
  if (( EUID == 0 )); then
    runuser -u "$SERVICE_USER" -- "$@"
  else
    sudo -u "$SERVICE_USER" -- "$@"
  fi
}

read_compose_env_value() {
  local key="$1"

  rust_server_env_read_value "$RUST_SERVER_ENV_FILE" "$key"
}

read_compose_env_value_or_default() {
  local key="$1"
  local default_value="$2"
  local value

  if value="$(read_compose_env_value "$key")" && [[ -n "$value" ]]; then
    printf '%s\n' "$value"
  else
    printf '%s\n' "$default_value"
  fi
}

quote_systemd_value() {
  local value="$1"

  value="${value//%/%%}"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  value="${value//$'\n'/\\n}"
  printf '"%s"' "$value"
}

escape_systemd_path() {
  local path="$1"

  path="${path//%/%%}"
  path="${path//\\/\\\\}"
  path="${path// /\\x20}"
  path="${path//$'\t'/\\t}"
  path="${path//$'\n'/\\n}"
  printf '%s' "$path"
}

remove_temporary_control_configuration() {
  local temporary_file="$1"

  run_as_root rm -f -- "$temporary_file"
}

migrate_control_configuration() {
  local temporary_file

  if [[ -e "$CONTROL_CONFIGURATION_FILE" || -L "$CONTROL_CONFIGURATION_FILE" ]]; then
    [[ -f "$CONTROL_CONFIGURATION_FILE" && ! -L "$CONTROL_CONFIGURATION_FILE" ]] || \
      bootstrap_die "Canonical Control configuration is not a regular file: $CONTROL_CONFIGURATION_FILE"
    bootstrap_skip "Preserving authoritative canonical Control configuration: $CONTROL_CONFIGURATION_FILE"
    return
  fi

  if [[ ! -e "$LEGACY_CONTROL_CONFIGURATION_FILE" && ! -L "$LEGACY_CONTROL_CONFIGURATION_FILE" ]]; then
    bootstrap_info "Control will create its configuration when it first saves state: $CONTROL_CONFIGURATION_FILE"
    return
  fi

  [[ -f "$LEGACY_CONTROL_CONFIGURATION_FILE" && ! -L "$LEGACY_CONTROL_CONFIGURATION_FILE" ]] || \
    bootstrap_die "Legacy Control configuration is not a regular file: $LEGACY_CONTROL_CONFIGURATION_FILE"

  temporary_file="$(run_as_root mktemp "$CONTROL_STATE_DIRECTORY/.server-configuration.json.tmp.XXXXXX")" || \
    bootstrap_die "Failed to create a temporary Control configuration file"

  if ! run_as_root install -m 0600 -o "$SERVICE_USER" -g "$SERVICE_USER_GROUP" \
    -- "$LEGACY_CONTROL_CONFIGURATION_FILE" "$temporary_file"; then
    remove_temporary_control_configuration "$temporary_file"
    bootstrap_die "Failed to copy legacy Control configuration"
  fi

  # A hard link creates the canonical name atomically without replacing an
  # administrator-managed file that appeared during migration.
  if ! run_as_root ln -- "$temporary_file" "$CONTROL_CONFIGURATION_FILE"; then
    remove_temporary_control_configuration "$temporary_file"
    [[ -f "$CONTROL_CONFIGURATION_FILE" && ! -L "$CONTROL_CONFIGURATION_FILE" ]] || \
      bootstrap_die "Failed to create canonical Control configuration: $CONTROL_CONFIGURATION_FILE"
    bootstrap_skip "Canonical Control configuration appeared during migration; preserving it"
    return
  fi

  remove_temporary_control_configuration "$temporary_file" || \
    bootstrap_die "Canonical Control configuration was created but its temporary file could not be removed"
  bootstrap_info "Migrated Control configuration to $CONTROL_CONFIGURATION_FILE; preserved legacy file as a recoverable backup: $LEGACY_CONTROL_CONFIGURATION_FILE"
}

ensure_control_configuration_access() {
  # Control writes a temporary file beside its state and atomically renames it
  # into place. Its dedicated state directory avoids changing shared /etc access.
  run_as_root install -d -o "$SERVICE_USER" -g "$SERVICE_USER_GROUP" -m 0700 -- "$CONTROL_STATE_DIRECTORY" || \
    bootstrap_die "Failed to prepare the Control state directory: $CONTROL_STATE_DIRECTORY"
  run_as_service_user test -w "$CONTROL_STATE_DIRECTORY" || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot write Control state directory: $CONTROL_STATE_DIRECTORY"
  run_as_service_user test -x "$CONTROL_STATE_DIRECTORY" || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot traverse Control state directory: $CONTROL_STATE_DIRECTORY"

  migrate_control_configuration

  if [[ -e "$CONTROL_CONFIGURATION_FILE" || -L "$CONTROL_CONFIGURATION_FILE" ]]; then
    [[ -f "$CONTROL_CONFIGURATION_FILE" && ! -L "$CONTROL_CONFIGURATION_FILE" ]] || \
      bootstrap_die "Canonical Control configuration is not a regular file: $CONTROL_CONFIGURATION_FILE"
    run_as_root chown "$SERVICE_USER:$SERVICE_USER_GROUP" -- "$CONTROL_CONFIGURATION_FILE" || \
      bootstrap_die "Failed to assign Control configuration ownership: $CONTROL_CONFIGURATION_FILE"
    run_as_root chmod 0600 -- "$CONTROL_CONFIGURATION_FILE" || \
      bootstrap_die "Failed to set Control configuration permissions: $CONTROL_CONFIGURATION_FILE"
    run_as_service_user test -rw "$CONTROL_CONFIGURATION_FILE" || \
      bootstrap_die "Workspace owner $SERVICE_USER cannot read or write Control configuration: $CONTROL_CONFIGURATION_FILE"
  fi
}

write_service_unit() {
  local unit_path="$1"

  {
    printf '%s\n' '[Unit]'
    printf '%s\n' 'Description=RustServer.Control API'
    printf '%s\n' 'Wants=network-online.target'
    printf '%s\n' 'After=network-online.target docker.service'
    printf '\n[Service]\n'
    printf 'Type=%s\n' 'exec'
    printf 'User=%s\n' "$SERVICE_USER"
    printf 'WorkingDirectory=%s\n' "$(escape_systemd_path "$CONTROL_RELEASE_DIR")"
    printf 'EnvironmentFile=%s\n' "$RUSTSERVER_TOPOLOGY_FILE"
    printf 'EnvironmentFile=%s\n' "$RUST_SERVER_ENV_FILE"
    printf 'Environment=%s\n' "$(quote_systemd_value 'ASPNETCORE_ENVIRONMENT=Development')"
    printf 'Environment=%s\n' "$(quote_systemd_value "RustServerControl__RustServerComposePath=$RUST_SERVER_COMPOSE_FILE")"
    printf 'Environment=%s\n' "$(quote_systemd_value "RustServerControl__RustServerDataPath=$RUST_SERVER_PATH")"
    printf 'Environment=%s\n' "$(quote_systemd_value "RustServerControl__RustServerIdentity=$RUST_SERVER_IDENTITY")"
    printf 'Environment=%s\n' "$(quote_systemd_value "RustServerUpdate__SteamCmdComposePath=$STEAMCMD_COMPOSE_FILE")"
    printf 'Environment=%s\n' "$(quote_systemd_value "OxideUpdate__OxideComposePath=$OXIDE_COMPOSE_FILE")"
    printf 'Environment=%s\n' "$(quote_systemd_value "ServerConfiguration__StoragePath=$CONTROL_CONFIGURATION_FILE")"
    printf 'Environment=%s\n' "$(quote_systemd_value "ServerConfiguration__InitialDesired__Hostname=$SERVER_CONFIGURATION_HOSTNAME")"
    printf 'Environment=%s\n' "$(quote_systemd_value "ServerConfiguration__InitialDesired__Description=$SERVER_CONFIGURATION_DESCRIPTION")"
    printf 'Environment=%s\n' "$(quote_systemd_value "ServerConfiguration__InitialDesired__MaxPlayers=$SERVER_CONFIGURATION_MAX_PLAYERS")"
    printf 'Environment=%s\n' "$(quote_systemd_value "ServerConfiguration__InitialDesired__TickRate=$SERVER_CONFIGURATION_TICK_RATE")"
    printf 'Environment=%s\n' "$(quote_systemd_value "ServerConfiguration__InitialDesired__SaveInterval=$SERVER_CONFIGURATION_SAVE_INTERVAL")"
    printf 'Environment=%s\n' "$(quote_systemd_value "ServerConfiguration__InitialDesired__Seed=$SERVER_CONFIGURATION_SEED")"
    printf 'Environment=%s\n' "$(quote_systemd_value "ServerConfiguration__InitialDesired__WorldSize=$SERVER_CONFIGURATION_WORLD_SIZE")"
    printf 'ExecStart=%s %s\n' \
      "$(escape_systemd_path "$DOTNET_EXECUTABLE")" \
      "$(quote_systemd_value "$CONTROL_DLL")"
    printf '%s\n' 'Restart=on-failure'
    printf '%s\n' 'RestartSec=5s'
    printf '%s\n' 'StandardOutput=journal'
    printf '%s\n' 'StandardError=journal'
    printf '%s\n' 'SyslogIdentifier=rustserver-control'
    printf '\n[Install]\n'
    printf '%s\n' 'WantedBy=multi-user.target'
  } > "$unit_path"
}

validate_docker_access() {
  local rcon_port

  rcon_port="$(topology_rcon_port)" || return 1

  if ! run_as_service_user docker version --format '{{.Server.Version}}' >/dev/null; then
    bootstrap_die "Workspace owner $SERVICE_USER cannot access Docker Engine without root; add it to the docker group and start a new login session before rerunning bootstrap."
  fi

  run_as_service_user docker compose version >/dev/null || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot use the Docker Compose plugin"

  if ! run_as_service_user env "RUST_SERVER_RCON_PORT=$rcon_port" docker compose \
    --env-file "$RUST_SERVER_ENV_FILE" \
    -f "$RUST_SERVER_COMPOSE_FILE" \
    config --quiet; then
    bootstrap_die "Workspace owner $SERVICE_USER cannot validate the Rust server Compose configuration"
  fi

  if ! run_as_service_user env "RUST_SERVER_RCON_PORT=$rcon_port" docker compose \
    --env-file "$RUST_SERVER_ENV_FILE" \
    -f "$STEAMCMD_COMPOSE_FILE" \
    config --quiet; then
    bootstrap_die "Workspace owner $SERVICE_USER cannot validate the SteamCMD Compose configuration"
  fi

  if ! run_as_service_user env "RUST_SERVER_PATH=$RUST_SERVER_PATH" docker compose \
    -f "$OXIDE_COMPOSE_FILE" \
    config --quiet; then
    bootstrap_die "Workspace owner $SERVICE_USER cannot validate the Oxide Compose configuration"
  fi
}

for required_command in awk chmod chown cmp dotnet env id install ln mktemp readlink rm runuser stat systemctl; do
  require_command "$required_command"
done

[[ -d /run/systemd/system ]] || \
  bootstrap_die "systemd is unavailable; cannot install $SERVICE_NAME"

ensure_sudo_access
load_topology_environment

for required_file in "$CONTROL_DLL" "$RUST_SERVER_COMPOSE_FILE" "$RUST_SERVER_ENV_FILE" "$STEAMCMD_COMPOSE_FILE" "$OXIDE_COMPOSE_FILE"; do
  require_regular_file "$required_file"
done
[[ ! -L "$RUST_SERVER_ENV_FILE" ]] || \
  bootstrap_die "Canonical Rust server configuration must not be a symbolic link: $RUST_SERVER_ENV_FILE"

readonly SERVICE_USER="$(stat -c '%U' -- "$WORKSPACE_ROOT")"
readonly SERVICE_USER_ID="$(stat -c '%u' -- "$WORKSPACE_ROOT")"

[[ "$SERVICE_USER_ID" != "0" && "$SERVICE_USER" != "root" ]] || \
  bootstrap_die "Workspace root must be owned by a non-root service user: $WORKSPACE_ROOT"
id "$SERVICE_USER" >/dev/null 2>&1 || \
  bootstrap_die "Workspace owner is not a local user: $SERVICE_USER"
readonly SERVICE_USER_GROUP="$(id -gn "$SERVICE_USER")"

ensure_control_configuration_access

readonly RUST_SERVER_PATH="$(read_compose_env_value 'RUST_SERVER_PATH')" || \
  bootstrap_die "RUST_SERVER_PATH is missing from canonical configuration: $RUST_SERVER_ENV_FILE"
[[ -n "$RUST_SERVER_PATH" ]] || \
  bootstrap_die "RUST_SERVER_PATH is empty in canonical configuration: $RUST_SERVER_ENV_FILE"
[[ "$RUST_SERVER_PATH" == /* ]] || \
  bootstrap_die "RUST_SERVER_PATH must be an absolute path for $SERVICE_NAME: $RUST_SERVER_PATH"
readonly RUST_SERVER_IDENTITY="$(read_compose_env_value 'RUST_SERVER_IDENTITY')" || \
  bootstrap_die "RUST_SERVER_IDENTITY is missing from canonical configuration: $RUST_SERVER_ENV_FILE"
[[ "$RUST_SERVER_IDENTITY" =~ ^[A-Za-z0-9_-]+$ ]] || \
  bootstrap_die "RUST_SERVER_IDENTITY must contain only letters, digits, underscores, or hyphens"
readonly SERVER_CONFIGURATION_HOSTNAME="$(read_compose_env_value_or_default 'RUST_SERVER_HOSTNAME' 'Rust Server')"
readonly SERVER_CONFIGURATION_DESCRIPTION="$(read_compose_env_value_or_default 'RUST_SERVER_DESCRIPTION' '')"
readonly SERVER_CONFIGURATION_MAX_PLAYERS="$(read_compose_env_value_or_default 'RUST_SERVER_MAX_PLAYERS' '50')"
readonly SERVER_CONFIGURATION_TICK_RATE="$(read_compose_env_value_or_default 'RUST_SERVER_TICK_RATE' '30')"
readonly SERVER_CONFIGURATION_SAVE_INTERVAL="$(read_compose_env_value_or_default 'RUST_SERVER_SAVE_INTERVAL' '600')"
readonly SERVER_CONFIGURATION_SEED="$(read_compose_env_value_or_default 'RUST_SERVER_SEED' '0')"
readonly SERVER_CONFIGURATION_WORLD_SIZE="$(read_compose_env_value_or_default 'RUST_SERVER_WORLD_SIZE' '3500')"

readonly DOTNET_EXECUTABLE="$(readlink -f -- "$(command -v dotnet)")"
[[ -x "$DOTNET_EXECUTABLE" ]] || \
  bootstrap_die "Resolved dotnet executable is not executable: $DOTNET_EXECUTABLE"

for required_readable_file in "$CONTROL_DLL" "$RUST_SERVER_COMPOSE_FILE" "$RUST_SERVER_ENV_FILE" "$STEAMCMD_COMPOSE_FILE" "$OXIDE_COMPOSE_FILE"; do
  run_as_service_user test -r "$required_readable_file" || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot read required file: $required_readable_file"
done
run_as_service_user test -x "$DOTNET_EXECUTABLE" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot execute dotnet: $DOTNET_EXECUTABLE"

validate_docker_access

readonly UNIT_TEMPORARY_PATH="$(mktemp)"
trap 'rm -f -- "$UNIT_TEMPORARY_PATH"' EXIT
write_service_unit "$UNIT_TEMPORARY_PATH"

unit_changed=0
if run_as_root test -f "$SERVICE_UNIT_PATH" && \
  run_as_root cmp -s "$UNIT_TEMPORARY_PATH" "$SERVICE_UNIT_PATH" && \
  [[ "$(run_as_root stat -c '%a' -- "$SERVICE_UNIT_PATH")" == "600" ]]; then
  bootstrap_skip "Systemd unit is already current: $SERVICE_UNIT_PATH"
else
  run_as_root install -m 0600 -- "$UNIT_TEMPORARY_PATH" "$SERVICE_UNIT_PATH"
  unit_changed=1
  bootstrap_info "Installed systemd unit: $SERVICE_UNIT_PATH"
fi

if (( unit_changed )); then
  run_as_root systemctl daemon-reload || \
    bootstrap_die "systemd daemon-reload failed after updating $SERVICE_UNIT_PATH"
fi

run_as_root systemctl enable "$SERVICE_NAME" || \
  bootstrap_die "Failed to enable $SERVICE_NAME"
run_as_root systemctl is-enabled --quiet "$SERVICE_NAME" || \
  bootstrap_die "$SERVICE_NAME is not enabled for startup"

if run_as_root systemctl is-active --quiet "$SERVICE_NAME"; then
  service_action="restart"
else
  service_action="start"
fi

if ! run_as_root systemctl "$service_action" "$SERVICE_NAME"; then
  run_as_root systemctl status "$SERVICE_NAME" --no-pager || true
  bootstrap_die "Failed to $service_action $SERVICE_NAME"
fi

if ! run_as_root systemctl is-active --quiet "$SERVICE_NAME"; then
  run_as_root systemctl status "$SERVICE_NAME" --no-pager || true
  bootstrap_die "$SERVICE_NAME did not become active"
fi

bootstrap_info "$SERVICE_NAME is active; API listens on $RUSTSERVER_CONTROL_URL"
