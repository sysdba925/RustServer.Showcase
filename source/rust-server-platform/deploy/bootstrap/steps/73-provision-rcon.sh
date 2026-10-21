#!/usr/bin/env bash
# Provisions the immutable-release and systemd boundaries required by
# RustServer.Rcon. It never enables, starts, stops, or restarts the service.
set -Eeuo pipefail

STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly STEP_DIR
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"
# shellcheck source=../lib/rcon-secret-permissions.sh
source "$STEP_DIR/../lib/rcon-secret-permissions.sh"

if ! bootstrap_mode_is install; then
  bootstrap_skip "Step is not required for Bootstrap mode: $(bootstrap_mode)"
  exit 0
fi

readonly RCON_USER="${RUSTSERVER_RCON_SERVICE_USER:-rustserver-rcon}"
readonly RCON_GROUP="${RUSTSERVER_RCON_SERVICE_GROUP:-rustserver-rcon}"
readonly RCON_INSTALLATION_DIRECTORY="${RUSTSERVER_RCON_INSTALLATION_DIRECTORY:-/opt/rustserver/rcon}"
readonly RCON_RELEASES_DIRECTORY="$RCON_INSTALLATION_DIRECTORY/releases"
readonly RCON_SYSTEMD_DIRECTORY="${RUSTSERVER_RCON_SYSTEMD_DIRECTORY:-/etc/systemd/system}"
readonly RCON_SERVICE_FILE="$RCON_SYSTEMD_DIRECTORY/rustserver-rcon-grpc.service"
readonly RCON_SERVICE_SOURCE="$STEP_DIR/../assets/rustserver-rcon-grpc.service"
readonly RCON_CONFIGURATION_DIRECTORY="${RUSTSERVER_RCON_CONFIGURATION_DIRECTORY:-/etc/rustserver}"
readonly RCON_TOPOLOGY_FILE="$RCON_CONFIGURATION_DIRECTORY/topology.env"
readonly RCON_TOPOLOGY_TEMPLATE="$STEP_DIR/../../../config/topology.env.example"
readonly RCON_PASSWORD_FILE="$RCON_CONFIGURATION_DIRECTORY/rcon-password"

create_rcon_user_if_missing() {
  if getent passwd "$RCON_USER" >/dev/null; then
    bootstrap_skip "Preserving existing Rcon service user: $RCON_USER"
    return
  fi

  run_as_root useradd --system --user-group --home-dir "$RCON_INSTALLATION_DIRECTORY" \
    --shell /usr/sbin/nologin "$RCON_USER" || \
    bootstrap_die "Could not create Rcon service user: $RCON_USER"
  bootstrap_info "Created dedicated Rcon service user: $RCON_USER"
}

create_rcon_service_unit_without_overwrite() {
  local temporary_file

  if root_path_exists "$RCON_SERVICE_FILE"; then
    require_root_regular_file "$RCON_SERVICE_FILE"
    bootstrap_skip "Preserving existing Rcon systemd unit: $RCON_SERVICE_FILE"
    return
  fi

  require_regular_file "$RCON_SERVICE_SOURCE"
  require_directory "$RCON_SYSTEMD_DIRECTORY"
  temporary_file="$(run_as_root mktemp --tmpdir="$RCON_SYSTEMD_DIRECTORY" '.rustserver-rcon-grpc.service.tmp.XXXXXX')" || \
    bootstrap_die 'Could not create a temporary Rcon systemd unit'
  if ! run_as_root install -m 0644 -o root -g root "$RCON_SERVICE_SOURCE" "$temporary_file"; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not write temporary Rcon systemd unit'
  fi

  # link(2) keeps administrator-provided units intact if a concurrent
  # provisioning run creates the final path first.
  if ! run_as_root ln -- "$temporary_file" "$RCON_SERVICE_FILE"; then
    run_as_root rm -f -- "$temporary_file"
    if root_path_exists "$RCON_SERVICE_FILE"; then
      require_root_regular_file "$RCON_SERVICE_FILE"
      bootstrap_skip "Preserving concurrently created Rcon systemd unit: $RCON_SERVICE_FILE"
      return
    fi
    bootstrap_die "Could not create Rcon systemd unit: $RCON_SERVICE_FILE"
  fi

  run_as_root rm -f -- "$temporary_file"
  bootstrap_info "Created Rcon systemd unit: $RCON_SERVICE_FILE"
}

provision_rcon_directories() {
  ensure_root_directory "$RCON_INSTALLATION_DIRECTORY" 0755 root root
  ensure_root_directory "$RCON_RELEASES_DIRECTORY" 0755 root root
  ensure_root_directory "$RCON_CONFIGURATION_DIRECTORY" 0755 root root
}

remove_temporary_rcon_configuration_file() {
  run_as_root rm -f -- "$1"
}

create_rcon_topology_without_overwrite() {
  local temporary_file

  if root_path_exists "$RCON_TOPOLOGY_FILE"; then
    require_root_regular_file "$RCON_TOPOLOGY_FILE"
    bootstrap_skip "Preserving existing topology configuration: $RCON_TOPOLOGY_FILE"
    return
  fi

  require_regular_file "$RCON_TOPOLOGY_TEMPLATE"
  require_directory "$RCON_CONFIGURATION_DIRECTORY"
  temporary_file="$(run_as_root mktemp --tmpdir="$RCON_CONFIGURATION_DIRECTORY" '.topology.env.tmp.XXXXXX')" || \
    bootstrap_die 'Could not create a temporary topology configuration file'

  if ! sed \
    -e 's/^# TEMPORARY DEVELOPMENT CONFIGURATION$/# PRODUCTION CONFIGURATION/' \
    -e 's/^# All HTTP services are reachable from the trusted development network\.$/# Backend HTTP services remain loopback-only./' \
    -e 's/^# Before public deployment, change the five backend bind hosts to 127\.0\.0\.1\.$/# RustServer.Web is served through Nginx./' \
    -e '/^# Web remains accessible externally\.$/d' \
    -e 's/^RUSTSERVER_CONTROL_BIND_HOST=.*/RUSTSERVER_CONTROL_BIND_HOST=127.0.0.1/' \
    -e 's/^RUSTSERVER_VERSIONS_BIND_HOST=.*/RUSTSERVER_VERSIONS_BIND_HOST=127.0.0.1/' \
    -e 's/^RUSTSERVER_PLUGINS_BIND_HOST=.*/RUSTSERVER_PLUGINS_BIND_HOST=127.0.0.1/' \
    -e 's/^RUSTSERVER_SNAPSHOTS_BIND_HOST=.*/RUSTSERVER_SNAPSHOTS_BIND_HOST=127.0.0.1/' \
    -e 's/^RUSTSERVER_WEB_BIND_HOST=.*/RUSTSERVER_WEB_BIND_HOST=127.0.0.1/' \
    "$RCON_TOPOLOGY_TEMPLATE" | run_as_root tee "$temporary_file" >/dev/null; then
    remove_temporary_rcon_configuration_file "$temporary_file"
    bootstrap_die 'Could not write temporary topology configuration file'
  fi

  if ! run_as_root chmod 0644 -- "$temporary_file"; then
    remove_temporary_rcon_configuration_file "$temporary_file"
    bootstrap_die 'Could not protect temporary topology configuration file'
  fi

  # link(2) is atomic and refuses to overwrite administrator-provided
  # configuration created concurrently.
  if ! run_as_root ln -- "$temporary_file" "$RCON_TOPOLOGY_FILE"; then
    remove_temporary_rcon_configuration_file "$temporary_file"
    if root_path_exists "$RCON_TOPOLOGY_FILE"; then
      require_root_regular_file "$RCON_TOPOLOGY_FILE"
      bootstrap_skip "Preserving concurrently created topology configuration: $RCON_TOPOLOGY_FILE"
      return
    fi
    bootstrap_die "Could not create topology configuration: $RCON_TOPOLOGY_FILE"
  fi

  remove_temporary_rcon_configuration_file "$temporary_file"
  bootstrap_info "Created Production topology configuration: $RCON_TOPOLOGY_FILE"
}

rcon_password_file_is_valid() {
  local password_file="$1"

  rcon_secret_permissions_are_valid "$password_file" || return 1
  run_as_root awk '
    NR != 1 || length($0) == 0 { invalid = 1 }
    END { exit invalid || NR != 1 }
  ' "$password_file"
}

create_rcon_password_without_overwrite() {
  local temporary_file

  if root_path_exists "$RCON_PASSWORD_FILE"; then
    require_root_regular_file "$RCON_PASSWORD_FILE"
    rcon_password_file_is_valid "$RCON_PASSWORD_FILE" || \
      bootstrap_die "Existing Rcon password file must contain one non-empty line: $RCON_PASSWORD_FILE"
    bootstrap_skip "Preserving existing Rcon password file: $RCON_PASSWORD_FILE"
    return
  fi

  require_command openssl
  require_directory "$RCON_CONFIGURATION_DIRECTORY"
  temporary_file="$(run_as_root mktemp --tmpdir="$RCON_CONFIGURATION_DIRECTORY" '.rcon-password.tmp.XXXXXX')" || \
    bootstrap_die 'Could not create a temporary Rcon password file'

  if ! openssl rand -hex 32 | run_as_root tee "$temporary_file" >/dev/null; then
    remove_temporary_rcon_configuration_file "$temporary_file"
    bootstrap_die 'Could not generate the Rcon password'
  fi

  if ! rcon_password_file_is_valid "$temporary_file"; then
    remove_temporary_rcon_configuration_file "$temporary_file"
    bootstrap_die 'Generated Rcon password is invalid'
  fi

  if ! run_as_root chmod 0600 -- "$temporary_file"; then
    remove_temporary_rcon_configuration_file "$temporary_file"
    bootstrap_die 'Could not protect temporary Rcon password file'
  fi

  if ! run_as_root ln -- "$temporary_file" "$RCON_PASSWORD_FILE"; then
    remove_temporary_rcon_configuration_file "$temporary_file"
    if root_path_exists "$RCON_PASSWORD_FILE"; then
      require_root_regular_file "$RCON_PASSWORD_FILE"
      rcon_password_file_is_valid "$RCON_PASSWORD_FILE" || \
        bootstrap_die "Concurrently created Rcon password file must contain one non-empty line: $RCON_PASSWORD_FILE"
      bootstrap_skip "Preserving concurrently created Rcon password file: $RCON_PASSWORD_FILE"
      return
    fi
    bootstrap_die "Could not create Rcon password file: $RCON_PASSWORD_FILE"
  fi

  remove_temporary_rcon_configuration_file "$temporary_file"
  bootstrap_info "Generated shared Rcon password file: $RCON_PASSWORD_FILE"
}

provision_rcon_runtime_configuration() {
  create_rcon_topology_without_overwrite
  create_rcon_password_without_overwrite
}

main() {
  ensure_sudo_access
  create_rcon_user_if_missing
  provision_rcon_directories
  provision_rcon_runtime_configuration
  create_rcon_service_unit_without_overwrite

  # Reload unit metadata only. Rcon stays inactive until an explicit Updater
  # Install operation has a verified package and passes its production guard.
  run_as_root systemctl daemon-reload || \
    bootstrap_die 'Could not reload systemd units after Rcon provisioning'
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
