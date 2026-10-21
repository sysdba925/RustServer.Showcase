#!/usr/bin/env bash
# Provisions the immutable-release and persistent-state boundaries required by
# RustServer.Auth. It create-only provisions auth.env from the Installer's
# secure handoff and never enables, starts, stops, or restarts the Auth service.
set -Eeuo pipefail

STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly STEP_DIR
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"
# shellcheck source=../lib/auth-configuration.sh
source "$STEP_DIR/../lib/auth-configuration.sh"

if ! bootstrap_mode_is install; then
  bootstrap_skip "Step is not required for Bootstrap mode: $(bootstrap_mode)"
  exit 0
fi

readonly AUTH_USER="${RUSTSERVER_AUTH_SERVICE_USER:-rustserver-auth}"
readonly AUTH_GROUP="${RUSTSERVER_AUTH_SERVICE_GROUP:-rustserver-auth}"
readonly AUTH_STATE_DIRECTORY="${RUSTSERVER_AUTH_STATE_DIRECTORY:-/var/lib/rustserver/auth}"
readonly AUTH_INSTALLATION_DIRECTORY="${RUSTSERVER_AUTH_INSTALLATION_DIRECTORY:-/opt/rustserver/auth}"
readonly AUTH_RELEASES_DIRECTORY="$AUTH_INSTALLATION_DIRECTORY/releases"
readonly AUTH_SYSTEMD_DIRECTORY="${RUSTSERVER_AUTH_SYSTEMD_DIRECTORY:-/etc/systemd/system}"
readonly AUTH_SERVICE_FILE="$AUTH_SYSTEMD_DIRECTORY/rustserver-auth.service"
readonly AUTH_SERVICE_SOURCE="$STEP_DIR/../assets/rustserver-auth.service"
readonly AUTH_ENVIRONMENT_FILE="${RUSTSERVER_AUTH_ENVIRONMENT_FILE:-/etc/rustserver/auth.env}"
readonly AUTH_DATABASE_FILE="$AUTH_STATE_DIRECTORY/auth.db"

create_auth_user_if_missing() {
  if getent passwd "$AUTH_USER" >/dev/null; then
    bootstrap_skip "Preserving existing Auth service user: $AUTH_USER"
    return
  fi

  run_as_root useradd --system --user-group --home-dir "$AUTH_STATE_DIRECTORY" \
    --shell /usr/sbin/nologin "$AUTH_USER" || \
    bootstrap_die "Could not create Auth service user: $AUTH_USER"
  bootstrap_info "Created dedicated Auth service user: $AUTH_USER"
}

create_auth_service_unit_without_overwrite() {
  local temporary_file

  if root_path_exists "$AUTH_SERVICE_FILE"; then
    require_root_regular_file "$AUTH_SERVICE_FILE"
    bootstrap_skip "Preserving existing Auth systemd unit: $AUTH_SERVICE_FILE"
    return
  fi

  require_regular_file "$AUTH_SERVICE_SOURCE"
  require_directory "$AUTH_SYSTEMD_DIRECTORY"
  temporary_file="$(run_as_root mktemp --tmpdir="$AUTH_SYSTEMD_DIRECTORY" '.rustserver-auth.service.tmp.XXXXXX')" || \
    bootstrap_die 'Could not create a temporary Auth systemd unit'
  if ! run_as_root install -m 0644 -o root -g root "$AUTH_SERVICE_SOURCE" "$temporary_file"; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not write temporary Auth systemd unit'
  fi

  # link(2) keeps administrator-provided units intact if a concurrent
  # provisioning run creates the final path first.
  if ! run_as_root ln -- "$temporary_file" "$AUTH_SERVICE_FILE"; then
    run_as_root rm -f -- "$temporary_file"
    if root_path_exists "$AUTH_SERVICE_FILE"; then
      require_root_regular_file "$AUTH_SERVICE_FILE"
      bootstrap_skip "Preserving concurrently created Auth systemd unit: $AUTH_SERVICE_FILE"
      return
    fi
    bootstrap_die "Could not create Auth systemd unit: $AUTH_SERVICE_FILE"
  fi

  run_as_root rm -f -- "$temporary_file"
  bootstrap_info "Created Auth systemd unit: $AUTH_SERVICE_FILE"
}

provision_auth_directories() {
  ensure_root_directory "$AUTH_INSTALLATION_DIRECTORY" 0755 root root
  ensure_root_directory "$AUTH_RELEASES_DIRECTORY" 0755 root root
  ensure_root_directory "$AUTH_STATE_DIRECTORY" 0750 "$AUTH_USER" "$AUTH_GROUP"
}

provision_auth_environment() {
  create_auth_environment_without_overwrite \
    "$AUTH_ENVIRONMENT_FILE" \
    "$AUTH_GROUP" \
    "$AUTH_DATABASE_FILE" \
    "${RUSTSERVER_AUTH_PROVISIONING_FILE:-}"
}

main() {
  ensure_sudo_access
  create_auth_user_if_missing
  provision_auth_directories
  provision_auth_environment
  create_auth_service_unit_without_overwrite

  # Reload unit metadata only. Auth stays inactive until an explicit Updater
  # Install operation has a verified package and passes its production guard.
  run_as_root systemctl daemon-reload || \
    bootstrap_die 'Could not reload systemd units after Auth provisioning'
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
