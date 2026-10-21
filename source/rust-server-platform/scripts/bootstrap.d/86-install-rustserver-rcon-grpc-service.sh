#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly RCON_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Rcon"
readonly RCON_GRPC_PROJECT_DIR="$RCON_REPOSITORY_DIR/src/RustServer.Rcon.Grpc"
readonly RCON_GRPC_RELEASE_DIR="$RCON_GRPC_PROJECT_DIR/bin/Release/net10.0"
readonly RCON_GRPC_DLL="$RCON_GRPC_RELEASE_DIR/RustServer.Rcon.Grpc.dll"
readonly RCON_GRPC_DEPS_FILE="$RCON_GRPC_RELEASE_DIR/RustServer.Rcon.Grpc.deps.json"
readonly RCON_GRPC_RUNTIME_CONFIG_FILE="$RCON_GRPC_RELEASE_DIR/RustServer.Rcon.Grpc.runtimeconfig.json"
readonly RCON_PASSWORD_FILE="/etc/rustserver/rcon-password"
readonly SERVICE_NAME="rustserver-rcon-grpc.service"
readonly SERVICE_UNIT_PATH="/etc/systemd/system/$SERVICE_NAME"
readonly RCON_GRPC_READINESS_ATTEMPTS=20
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
    printf '%s\n' 'Description=RustServer.Rcon gRPC host'
    printf '%s\n' 'Wants=network-online.target'
    printf '%s\n' 'After=network-online.target'
    printf '\n[Service]\n'
    printf '%s\n' 'Type=exec'
    printf 'User=%s\n' "$SERVICE_USER"
    printf 'WorkingDirectory=%s\n' "$(escape_systemd_path "$RCON_GRPC_RELEASE_DIR")"
    printf 'EnvironmentFile=%s\n' "$RUSTSERVER_TOPOLOGY_FILE"
    printf 'Environment=%s\n' "$(quote_systemd_value 'ASPNETCORE_ENVIRONMENT=Development')"
    printf 'ExecStart=%s -c %s -- %s %s %s\n' \
      "$(escape_systemd_path "$BASH_EXECUTABLE")" \
      "$(quote_systemd_value "$(rcon_password_exec_script)")" \
      "$(quote_systemd_value "$RCON_PASSWORD_FILE")" \
      "$(quote_systemd_value "$DOTNET_EXECUTABLE")" \
      "$(quote_systemd_value "$RCON_GRPC_DLL")"
    printf '%s\n' 'Restart=on-failure'
    printf '%s\n' 'RestartSec=5s'
    printf '%s\n' 'StandardOutput=journal'
    printf '%s\n' 'StandardError=journal'
    printf '%s\n' 'SyslogIdentifier=rustserver-rcon-grpc'
    printf '\n[Install]\n'
    printf '%s\n' 'WantedBy=multi-user.target'
  } > "$unit_path"
}

rcon_grpc_endpoint_is_reachable() {
  local response_status

  response_status="$(curl --http2-prior-knowledge --silent --max-time 5 \
    --output /dev/null --write-out '%{http_code}' "$RUSTSERVER_RCON_GRPC_URL")" || return 1
  [[ "$response_status" =~ ^[1-5][0-9]{2}$ ]]
}

rcon_grpc_openapi_is_available() {
  curl --fail --silent --max-time 5 --output /dev/null "$RCON_GRPC_OPENAPI_URL"
}

wait_for_rcon_grpc_readiness() {
  local attempt

  for ((attempt = 1; attempt <= RCON_GRPC_READINESS_ATTEMPTS; attempt++)); do
    if rcon_grpc_endpoint_is_reachable && rcon_grpc_openapi_is_available; then
      return 0
    fi

    sleep 1
  done

  return 1
}

for required_command in bash cmp curl dotnet id install mktemp readlink runuser sleep stat systemctl; do
  require_command "$required_command"
done

[[ -d /run/systemd/system ]] || \
  bootstrap_die "systemd is unavailable; cannot install $SERVICE_NAME"

ensure_sudo_access
load_topology_environment
readonly RCON_GRPC_OPENAPI_URL="${RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL%/}/openapi.json"

for required_file in "$RCON_GRPC_DLL" "$RCON_GRPC_DEPS_FILE" "$RCON_GRPC_RUNTIME_CONFIG_FILE"; do
  require_nonempty_file "$required_file"
done

readonly SERVICE_USER="$(stat -c '%U' -- "$WORKSPACE_ROOT")"
readonly SERVICE_USER_ID="$(stat -c '%u' -- "$WORKSPACE_ROOT")"

[[ "$SERVICE_USER_ID" != "0" && "$SERVICE_USER" != "root" ]] || \
  bootstrap_die "Workspace root must be owned by a non-root service user: $WORKSPACE_ROOT"
id "$SERVICE_USER" >/dev/null 2>&1 || \
  bootstrap_die "Workspace owner is not a local user: $SERVICE_USER"

validate_rcon_password_file

readonly BASH_EXECUTABLE="$(readlink -f -- "$(command -v bash)")"
readonly DOTNET_EXECUTABLE="$(readlink -f -- "$(command -v dotnet)")"
[[ -x "$BASH_EXECUTABLE" ]] || \
  bootstrap_die "Resolved bash executable is not executable: $BASH_EXECUTABLE"
[[ -x "$DOTNET_EXECUTABLE" ]] || \
  bootstrap_die "Resolved dotnet executable is not executable: $DOTNET_EXECUTABLE"

for required_readable_file in "$RCON_GRPC_DLL" "$RCON_GRPC_DEPS_FILE" "$RCON_GRPC_RUNTIME_CONFIG_FILE" "$RCON_PASSWORD_FILE"; do
  run_as_service_user test -r "$required_readable_file" || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot read required file: $required_readable_file"
done
run_as_service_user test -x "$RCON_GRPC_RELEASE_DIR" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot traverse RCON gRPC release directory: $RCON_GRPC_RELEASE_DIR"
for required_executable in "$BASH_EXECUTABLE" "$DOTNET_EXECUTABLE"; do
  run_as_service_user test -x "$required_executable" || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot execute required file: $required_executable"
done

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

if ! wait_for_rcon_grpc_readiness; then
  run_as_root systemctl status "$SERVICE_NAME" --no-pager || true
  bootstrap_die "$SERVICE_NAME started but gRPC or OpenAPI is unavailable"
fi

bootstrap_info "$SERVICE_NAME is active; gRPC listens on $RUSTSERVER_RCON_GRPC_URL and OpenAPI is available at $RCON_GRPC_OPENAPI_URL"
