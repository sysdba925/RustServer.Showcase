#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly SNAPSHOTS_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Snapshots"
readonly SNAPSHOTS_API_PROJECT_DIR="$SNAPSHOTS_REPOSITORY_DIR/src/RustServer.Snapshots.Api"
readonly SNAPSHOTS_RELEASE_DIR="$SNAPSHOTS_API_PROJECT_DIR/bin/Release/net10.0"
readonly SNAPSHOTS_DLL="$SNAPSHOTS_RELEASE_DIR/RustServer.Snapshots.Api.dll"
readonly RUST_SERVER_ENV_FILE="/etc/rustserver/rust-server.env"
readonly RCON_PASSWORD_FILE="/etc/rustserver/rcon-password"
readonly SNAPSHOTS_STORAGE_PATH="$WORKSPACE_ROOT/state/RustServer.Snapshots"
readonly SERVICE_NAME="rustserver-snapshots.service"
readonly SERVICE_UNIT_PATH="/etc/systemd/system/$SERVICE_NAME"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"
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

  [[ -f "$RUST_SERVER_ENV_FILE" && ! -L "$RUST_SERVER_ENV_FILE" ]] || return 1
  [[ "$key" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || return 2

  awk -v key="$key" '
    function trim(value) {
      sub(/^[[:space:]]+/, "", value)
      sub(/[[:space:]]+$/, "", value)
      return value
    }
    {
      line = $0
      sub(/\r$/, "", line)
      if (line ~ /^[[:space:]]*#/ || line ~ /^[[:space:]]*$/) next

      pattern = "^[[:space:]]*" key "[[:space:]]*="
      if (line ~ pattern) {
        value = trim(line)
        sub(pattern, "", value)
        value = trim(value)
        if ((value ~ /^".*"$/) || (value ~ /^\047.*\047$/)) {
          value = substr(value, 2, length(value) - 2)
        } else {
          sub(/[[:space:]]+#.*$/, "", value)
          value = trim(value)
        }
        found = 1
      }
    }
    END {
      if (found) print value
      else exit 1
    }
  ' "$RUST_SERVER_ENV_FILE"
}

validate_rcon_password_file() {
  [[ -f "$RCON_PASSWORD_FILE" && ! -L "$RCON_PASSWORD_FILE" ]] || \
    bootstrap_die "RCON password secret file is missing or not a regular file: $RCON_PASSWORD_FILE"
  run_as_root test -s "$RCON_PASSWORD_FILE" || \
    bootstrap_die "RCON password secret file is empty: $RCON_PASSWORD_FILE"
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

rcon_password_exec_script() {
  printf '%s\n' \
    'IFS= read -r RustServerRcon__Password < "$$1" || true' \
    "RustServerRcon__Password=\"\$\${RustServerRcon__Password%\$\$'\\r'}\"" \
    'if [[ -z "$$RustServerRcon__Password" ]]; then' \
    '  printf "RCON password secret file is empty: %s\\n" "$$1" >&2' \
    '  exit 1' \
    'fi' \
    'export RustServerRcon__Password' \
    'exec "$$2" "$$3"'
}

write_service_unit() {
  local unit_path="$1"

  {
    printf '%s\n' '[Unit]'
    printf '%s\n' 'Description=RustServer.Snapshots API'
    printf '%s\n' 'Wants=network-online.target'
    printf '%s\n' 'After=network-online.target rustserver-control.service'
    printf '\n[Service]\n'
    printf 'Type=%s\n' 'exec'
    printf 'User=%s\n' "$SERVICE_USER"
    printf 'WorkingDirectory=%s\n' "$(escape_systemd_path "$SNAPSHOTS_RELEASE_DIR")"
    printf 'EnvironmentFile=%s\n' "$RUSTSERVER_TOPOLOGY_FILE"
    printf 'Environment=%s\n' "$(quote_systemd_value 'ASPNETCORE_ENVIRONMENT=Development')"
    printf 'Environment=%s\n' "$(quote_systemd_value "Snapshots__ServerIdentityPath=$SERVER_IDENTITY_PATH")"
    printf 'Environment=%s\n' "$(quote_systemd_value "Snapshots__OxideDataPath=$OXIDE_DATA_PATH")"
    printf 'Environment=%s\n' "$(quote_systemd_value "Snapshots__OxideConfigPath=$OXIDE_CONFIG_PATH")"
    printf 'Environment=%s\n' "$(quote_systemd_value "Snapshots__OxidePluginsPath=$OXIDE_PLUGINS_PATH")"
    printf 'Environment=%s\n' "$(quote_systemd_value "Snapshots__OxideLangPath=$OXIDE_LANG_PATH")"
    printf 'Environment=%s\n' "$(quote_systemd_value "Snapshots__StoragePath=$SNAPSHOTS_STORAGE_PATH")"
    printf 'ExecStart=%s -c %s -- %s %s %s\n' \
      "$(escape_systemd_path "$BASH_EXECUTABLE")" \
      "$(quote_systemd_value "$(rcon_password_exec_script)")" \
      "$(quote_systemd_value "$RCON_PASSWORD_FILE")" \
      "$(quote_systemd_value "$DOTNET_EXECUTABLE")" \
      "$(quote_systemd_value "$SNAPSHOTS_DLL")"
    printf '%s\n' 'Restart=on-failure'
    printf '%s\n' 'RestartSec=5s'
    printf '%s\n' 'StandardOutput=journal'
    printf '%s\n' 'StandardError=journal'
    printf '%s\n' 'SyslogIdentifier=rustserver-snapshots'
    printf '\n[Install]\n'
    printf '%s\n' 'WantedBy=multi-user.target'
  } > "$unit_path"
}

ensure_directory_access() {
  local directory="$1"

  [[ -d "$directory" ]] || \
    bootstrap_die "Configured snapshot source directory is missing: $directory"
  run_as_service_user test -r "$directory" || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot read snapshot source directory: $directory"
  run_as_service_user test -x "$directory" || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot traverse snapshot source directory: $directory"
}

for required_command in awk bash cmp curl dotnet id install mktemp readlink runuser stat systemctl; do
  require_command "$required_command"
done

[[ -d /run/systemd/system ]] || \
  bootstrap_die "systemd is unavailable; cannot install $SERVICE_NAME"

ensure_sudo_access
load_topology_environment
require_regular_file "$RUST_SERVER_ENV_FILE"
validate_rcon_password_file
[[ -f "$SNAPSHOTS_DLL" ]] || \
  bootstrap_die "Expected RustServer.Snapshots Release DLL is missing: $SNAPSHOTS_DLL. Step 96 must build it before $SERVICE_NAME is installed."

readonly SERVICE_USER="$(stat -c '%U' -- "$WORKSPACE_ROOT")"
readonly SERVICE_USER_ID="$(stat -c '%u' -- "$WORKSPACE_ROOT")"

[[ "$SERVICE_USER_ID" != "0" && "$SERVICE_USER" != "root" ]] || \
  bootstrap_die "Workspace root must be owned by a non-root service user: $WORKSPACE_ROOT"
id "$SERVICE_USER" >/dev/null 2>&1 || \
  bootstrap_die "Workspace owner is not a local user: $SERVICE_USER"

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

readonly SERVER_IDENTITY_PATH="$RUST_SERVER_PATH/server/$RUST_SERVER_IDENTITY"
readonly OXIDE_DATA_PATH="$RUST_SERVER_PATH/oxide/data"
readonly OXIDE_CONFIG_PATH="$RUST_SERVER_PATH/oxide/config"
readonly OXIDE_PLUGINS_PATH="$RUST_SERVER_PATH/oxide/plugins"
readonly OXIDE_LANG_PATH="$RUST_SERVER_PATH/oxide/lang"

readonly BASH_EXECUTABLE="$(readlink -f -- "$(command -v bash)")"
readonly DOTNET_EXECUTABLE="$(readlink -f -- "$(command -v dotnet)")"
[[ -x "$BASH_EXECUTABLE" ]] || \
  bootstrap_die "Resolved bash executable is not executable: $BASH_EXECUTABLE"
[[ -x "$DOTNET_EXECUTABLE" ]] || \
  bootstrap_die "Resolved dotnet executable is not executable: $DOTNET_EXECUTABLE"

for required_readable_file in "$SNAPSHOTS_DLL" "$RUST_SERVER_ENV_FILE" "$RCON_PASSWORD_FILE"; do
  run_as_service_user test -r "$required_readable_file" || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot read required file: $required_readable_file"
done
for required_executable in "$BASH_EXECUTABLE" "$DOTNET_EXECUTABLE"; do
  run_as_service_user test -x "$required_executable" || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot execute required file: $required_executable"
done

ensure_directory_access "$SERVER_IDENTITY_PATH"
ensure_directory_access "$OXIDE_DATA_PATH"
ensure_directory_access "$OXIDE_CONFIG_PATH"
ensure_directory_access "$OXIDE_PLUGINS_PATH"
ensure_directory_access "$OXIDE_LANG_PATH"
run_as_service_user install -d -m 0750 -- "$SNAPSHOTS_STORAGE_PATH" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot create snapshot storage: $SNAPSHOTS_STORAGE_PATH"
run_as_service_user test -w "$SNAPSHOTS_STORAGE_PATH" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot write snapshot storage: $SNAPSHOTS_STORAGE_PATH"

readonly UNIT_TEMPORARY_PATH="$(mktemp)"
trap 'rm -f -- "$UNIT_TEMPORARY_PATH"' EXIT
write_service_unit "$UNIT_TEMPORARY_PATH"

unit_changed=0
if run_as_root test -f "$SERVICE_UNIT_PATH" && \
  run_as_root cmp -s "$UNIT_TEMPORARY_PATH" "$SERVICE_UNIT_PATH" && \
  [[ "$(run_as_root stat -c '%a' -- "$SERVICE_UNIT_PATH")" == "644" ]]; then
  bootstrap_skip "Systemd unit is already current: $SERVICE_UNIT_PATH"
else
  run_as_root install -m 0644 -- "$UNIT_TEMPORARY_PATH" "$SERVICE_UNIT_PATH"
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

for _ in {1..20}; do
  if curl --fail --silent --show-error "$RUSTSERVER_SNAPSHOTS_URL/openapi.json" >/dev/null; then
    bootstrap_info "$SERVICE_NAME is active; API listens on $RUSTSERVER_SNAPSHOTS_URL"
    exit 0
  fi

  sleep 0.5
done

bootstrap_die "$SERVICE_NAME started but OpenAPI is unavailable on $RUSTSERVER_SNAPSHOTS_URL/openapi.json"
