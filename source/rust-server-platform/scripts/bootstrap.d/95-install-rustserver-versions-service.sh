#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly VERSIONS_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Versions"
readonly VERSIONS_API_PROJECT_DIR="$VERSIONS_REPOSITORY_DIR/src/RustServer.Versions.Api"
readonly VERSIONS_RELEASE_DIR="$VERSIONS_API_PROJECT_DIR/bin/Release/net10.0"
readonly VERSIONS_DLL="$VERSIONS_RELEASE_DIR/RustServer.Versions.Api.dll"
readonly RUST_SERVER_ENV_FILE="/etc/rustserver/rust-server.env"
readonly SERVICE_NAME="rustserver-versions.service"
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

write_service_unit() {
  local unit_path="$1"

  {
    printf '%s\n' '[Unit]'
    printf '%s\n' 'Description=RustServer.Versions API'
    printf '%s\n' 'Wants=network-online.target'
    printf '%s\n' 'After=network-online.target'
    printf '\n[Service]\n'
    printf 'Type=%s\n' 'exec'
    printf 'User=%s\n' "$SERVICE_USER"
    printf 'WorkingDirectory=%s\n' "$(escape_systemd_path "$VERSIONS_RELEASE_DIR")"
    printf 'EnvironmentFile=%s\n' "$RUSTSERVER_TOPOLOGY_FILE"
    printf 'Environment=%s\n' "$(quote_systemd_value 'ASPNETCORE_ENVIRONMENT=Development')"
    printf 'Environment=%s\n' "$(quote_systemd_value "RustVersions__AppManifestPath=$RUST_APP_MANIFEST_PATH")"
    printf 'Environment=%s\n' "$(quote_systemd_value "OxideVersions__AssemblyPath=$OXIDE_ASSEMBLY_PATH")"
    printf 'ExecStart=%s %s\n' \
      "$(escape_systemd_path "$DOTNET_EXECUTABLE")" \
      "$(quote_systemd_value "$VERSIONS_DLL")"
    printf '%s\n' 'Restart=on-failure'
    printf '%s\n' 'RestartSec=5s'
    printf '%s\n' 'SyslogIdentifier=rustserver-versions'
    printf '\n[Install]\n'
    printf '%s\n' 'WantedBy=multi-user.target'
  } > "$unit_path"
}

for required_command in awk cmp dotnet id install mktemp readlink runuser stat systemctl; do
  require_command "$required_command"
done

[[ -d /run/systemd/system ]] || \
  bootstrap_die "systemd is unavailable; cannot install $SERVICE_NAME"

ensure_sudo_access
load_topology_environment
require_regular_file "$RUST_SERVER_ENV_FILE"
[[ -f "$VERSIONS_DLL" ]] || \
  bootstrap_die "Expected RustServer.Versions Release DLL is missing: $VERSIONS_DLL. Step 90 must build it before $SERVICE_NAME is installed."

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
readonly RUST_APP_MANIFEST_PATH="$RUST_SERVER_PATH/steamapps/appmanifest_258550.acf"
readonly OXIDE_ASSEMBLY_PATH="$RUST_SERVER_PATH/RustDedicated_Data/Managed/Oxide.Rust.dll"

readonly DOTNET_EXECUTABLE="$(readlink -f -- "$(command -v dotnet)")"
[[ -x "$DOTNET_EXECUTABLE" ]] || \
  bootstrap_die "Resolved dotnet executable is not executable: $DOTNET_EXECUTABLE"

for required_readable_file in "$VERSIONS_DLL" "$RUST_SERVER_ENV_FILE"; do
  run_as_service_user test -r "$required_readable_file" || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot read required file: $required_readable_file"
done

run_as_service_user test -x "$DOTNET_EXECUTABLE" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot execute dotnet: $DOTNET_EXECUTABLE"

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

bootstrap_info "$SERVICE_NAME is active; API listens on $RUSTSERVER_VERSIONS_URL"
