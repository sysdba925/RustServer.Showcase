#!/usr/bin/env bash
set -Eeuo pipefail

STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly STEP_DIR
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

if ! bootstrap_mode_is install; then
  bootstrap_skip "Step is not required for Bootstrap mode: $(bootstrap_mode)"
  exit 0
fi

readonly UPDATER_ENVIRONMENT_FILE="/etc/rustserver/updater.env"
readonly UPDATER_CONFIGURATION_DIRECTORY="/etc/rustserver"

validate_runtime_environment() {
  local bind_address
  local port

  bind_address="$(environment_value "$UPDATER_ENVIRONMENT_FILE" "RUSTSERVER_UPDATER_BIND_ADDRESS")"
  port="$(environment_value "$UPDATER_ENVIRONMENT_FILE" "RUSTSERVER_UPDATER_PORT")"
  validate_ipv4_address "$bind_address"
  validate_port "$port"
}

create_runtime_environment() {
  local temporary_file

  if root_path_exists "$UPDATER_ENVIRONMENT_FILE"; then
    require_root_regular_file "$UPDATER_ENVIRONMENT_FILE"
    validate_runtime_environment
    bootstrap_skip "Preserving existing Updater environment: $UPDATER_ENVIRONMENT_FILE"
    return
  fi

  [[ "$RUSTSERVER_UPDATER_BIND_ADDRESS" == 127.0.0.1 ]] || \
    bootstrap_die "New Production Updater configuration requires RUSTSERVER_UPDATER_BIND_ADDRESS=127.0.0.1"
  require_directory "$UPDATER_CONFIGURATION_DIRECTORY"
  temporary_file="$(run_as_root mktemp --tmpdir="$UPDATER_CONFIGURATION_DIRECTORY" '.updater.env.tmp.XXXXXX')" || \
    bootstrap_die "Could not create temporary Updater environment file"

  if ! {
    printf 'RUSTSERVER_UPDATER_BIND_ADDRESS=%s\n' "$RUSTSERVER_UPDATER_BIND_ADDRESS"
    printf 'RUSTSERVER_UPDATER_PORT=%s\n' "$RUSTSERVER_UPDATER_PORT"
  } | run_as_root tee "$temporary_file" >/dev/null; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die "Could not write temporary Updater environment file"
  fi

  run_as_root chmod 0640 -- "$temporary_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die "Could not protect temporary Updater environment file"
  }

  # link(2) is atomic and refuses to overwrite a file created concurrently.
  if ! run_as_root ln -- "$temporary_file" "$UPDATER_ENVIRONMENT_FILE"; then
    run_as_root rm -f -- "$temporary_file"
    if root_path_exists "$UPDATER_ENVIRONMENT_FILE"; then
      require_root_regular_file "$UPDATER_ENVIRONMENT_FILE"
      validate_runtime_environment
      bootstrap_skip "Preserving concurrently created Updater environment: $UPDATER_ENVIRONMENT_FILE"
      return
    fi
    bootstrap_die "Could not create Updater environment file: $UPDATER_ENVIRONMENT_FILE"
  fi

  run_as_root rm -f -- "$temporary_file"
  validate_runtime_environment
  bootstrap_info "Created Updater environment: $UPDATER_ENVIRONMENT_FILE"
}

ensure_sudo_access
load_bootstrap_defaults
validate_updater_configuration
create_runtime_environment
