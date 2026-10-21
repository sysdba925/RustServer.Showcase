#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly PLUGINS_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Plugins"
readonly PLUGINS_PROJECT_DIR="$PLUGINS_REPOSITORY_DIR/src/RustServer.Plugins"
readonly PLUGINS_RELEASE_DIR="$PLUGINS_PROJECT_DIR/bin/Release/net10.0"
readonly PLUGINS_DLL="$PLUGINS_RELEASE_DIR/RustServer.Plugins.dll"
readonly PLUGINS_STATE_ROOT="$WORKSPACE_ROOT/state/RustServer.Plugins"
readonly PLUGINS_OPERATION_HISTORY_DATABASE="$PLUGINS_STATE_ROOT/operations.db"
readonly RUST_SERVER_ENV_FILE="/etc/rustserver/rust-server.env"
readonly CONFIGS_WORKSPACE="$WORKSPACE_ROOT/oxide"
readonly PLUGINS_WORKSPACE="$CONFIGS_WORKSPACE/plugins"
readonly SERVICE_NAME="rustserver-plugins.service"
readonly SERVICE_UNIT_PATH="/etc/systemd/system/$SERVICE_NAME"
readonly PLUGINS_READINESS_ATTEMPTS=30
readonly PLUGINS_TOKEN_PATH='/etc/rustserver/plugins-github-token'
readonly UPDATER_TOKEN_PATH='/etc/rustserver/updater-github-token'
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

provision_plugins_github_token() {
  local input_file temporary_file owner group mode
  if run_as_root test -e "$PLUGINS_TOKEN_PATH" || run_as_root test -L "$PLUGINS_TOKEN_PATH"; then
    run_as_root test -f "$PLUGINS_TOKEN_PATH" &&
      ! run_as_root test -L "$PLUGINS_TOKEN_PATH" ||
      bootstrap_die 'Plugins credential must be a regular file, not a symbolic link'
  else
    input_file="${RUSTSERVER_BOOTSTRAP_PLUGINS_GITHUB_TOKEN_FILE:-}"
    if [[ -z "$input_file" ]] && run_as_root test -f "$UPDATER_TOKEN_PATH"; then
      input_file="$UPDATER_TOKEN_PATH"
    fi
    if [[ -z "$input_file" ]]; then
      bootstrap_die 'A Plugins GitHub token is required for first-run Clone. Set RUSTSERVER_BOOTSTRAP_PLUGINS_GITHUB_TOKEN_FILE to a private token file, or provide an existing /etc/rustserver/updater-github-token.'
    fi
    [[ -f "$input_file" && ! -L "$input_file" ]] ||
      bootstrap_die 'Plugins token source must be an existing regular file'
    mode="$(stat -c '%a' -- "$input_file")"
    (( (8#$mode & 077) == 0 )) ||
      bootstrap_die 'Plugins token source must be private'
    [[ "$(run_as_root cat -- "$input_file")" =~ ^[A-Za-z0-9_=-]+$ ]] ||
      bootstrap_die 'Plugins token source contains unsupported characters'
    run_as_root install -d -m 0755 -o root -g root /etc/rustserver ||
      bootstrap_die 'Could not create credential directory'
    temporary_file="$(run_as_root mktemp /etc/rustserver/.plugins-github-token.tmp.XXXXXX)" ||
      bootstrap_die 'Could not stage Plugins GitHub credential'
    if ! run_as_root install -m 0600 -o root -g root -- "$input_file" "$temporary_file"; then
      run_as_root rm -f -- "$temporary_file"
      bootstrap_die 'Could not securely stage Plugins GitHub credential'
    fi
    if ! run_as_root ln -- "$temporary_file" "$PLUGINS_TOKEN_PATH"; then
      run_as_root rm -f -- "$temporary_file"
      bootstrap_die 'Plugins credential was concurrently created; rerun Bootstrap'
    fi
    run_as_root rm -f -- "$temporary_file"
    bootstrap_info 'Created independent Plugins GitHub credential'
  fi
  owner="$(run_as_root stat -c '%U' -- "$PLUGINS_TOKEN_PATH")"
  group="$(run_as_root stat -c '%G' -- "$PLUGINS_TOKEN_PATH")"
  mode="$(run_as_root stat -c '%a' -- "$PLUGINS_TOKEN_PATH")"
  [[ "$owner" == root && "$group" == root && "$mode" == 600 ]] ||
    bootstrap_die 'Plugins credential must be root:root mode 0600'
  [[ "$(run_as_root cat -- "$PLUGINS_TOKEN_PATH")" =~ ^[A-Za-z0-9_=-]+$ ]] ||
    bootstrap_die 'Plugins credential contains invalid characters'
}

write_service_unit() {
  local unit_path="$1"

  {
    printf '%s\n' '[Unit]'
    printf '%s\n' 'Description=RustServer.Plugins API'
    printf '%s\n' 'Wants=network-online.target rustserver-control.service'
    printf '%s\n' 'After=network-online.target rustserver-control.service'
    printf '\n[Service]\n'
    printf '%s\n' 'Type=exec'
    printf 'User=%s\n' "$SERVICE_USER"
    printf 'WorkingDirectory=%s\n' "$(escape_systemd_path "$PLUGINS_RELEASE_DIR")"
    printf 'EnvironmentFile=%s\n' "$RUSTSERVER_TOPOLOGY_FILE"
    printf 'Environment=%s\n' "$(quote_systemd_value 'ASPNETCORE_ENVIRONMENT=Development')"
    printf 'Environment=%s\n' "$(quote_systemd_value "PluginManagement__StateRootPath=$PLUGINS_STATE_ROOT")"
    printf 'Environment=%s\n' "$(quote_systemd_value "PluginManagement__OperationHistory__DatabasePath=$PLUGINS_OPERATION_HISTORY_DATABASE")"
    printf 'Environment=%s\n' "$(quote_systemd_value "PluginManagement__Runtime__ServerRootPath=$RUST_SERVER_PATH")"
    printf 'Environment=%s\n' "$(quote_systemd_value "PluginManagement__Repositories__Configs__WorkspacePath=$CONFIGS_WORKSPACE")"
    printf 'Environment=%s\n' "$(quote_systemd_value "PluginManagement__Repositories__Plugins__WorkspacePath=$PLUGINS_WORKSPACE")"
    printf '%s\n' 'Environment=PluginManagement__WorkspaceInitialization__Enabled=true'
    printf '%s\n' 'LoadCredential=github-token:/etc/rustserver/plugins-github-token'
    printf '%s\n' 'Environment=RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE=%d/github-token'
    printf '%s\n' 'InaccessiblePaths=/etc/rustserver/plugins-github-token'
    printf 'ExecStart=%s %s\n' \
      "$(escape_systemd_path "$DOTNET_EXECUTABLE")" \
      "$(quote_systemd_value "$PLUGINS_DLL")"
    printf '%s\n' 'Restart=on-failure'
    printf '%s\n' 'RestartSec=5s'
    printf '%s\n' 'StandardOutput=journal'
    printf '%s\n' 'StandardError=journal'
    printf '%s\n' 'SyslogIdentifier=rustserver-plugins'
    printf '\n[Install]\n'
    printf '%s\n' 'WantedBy=multi-user.target'
  } > "$unit_path"
}

plugins_service_is_responding() {
  local response_status

  response_status="$(curl --silent --max-time 5 --output /dev/null --write-out '%{http_code}' "$PLUGINS_PROBE_URL")" || return 1
  [[ "$response_status" =~ ^[1-4][0-9]{2}$ ]]
}

wait_for_plugins_service_response() {
  local attempt

  for ((attempt = 1; attempt <= PLUGINS_READINESS_ATTEMPTS; attempt++)); do
    if plugins_service_is_responding; then
      return 0
    fi

    sleep 1
  done

  return 1
}

for required_command in awk cmp curl dotnet git install mktemp readlink runuser sleep stat systemctl; do
  require_command "$required_command"
done

[[ -d /run/systemd/system ]] || \
  bootstrap_die "systemd is unavailable; cannot install $SERVICE_NAME"

ensure_sudo_access
load_topology_environment
readonly PLUGINS_PROBE_URL="$RUSTSERVER_PLUGINS_URL"
require_regular_file "$RUST_SERVER_ENV_FILE"
[[ -f "$PLUGINS_DLL" ]] || \
  bootstrap_die "Expected RustServer.Plugins Release DLL is missing: $PLUGINS_DLL. Step 91 must build it before $SERVICE_NAME is installed."

readonly SERVICE_USER="$(stat -c '%U' -- "$WORKSPACE_ROOT")"
readonly SERVICE_USER_ID="$(stat -c '%u' -- "$WORKSPACE_ROOT")"
readonly SERVICE_GROUP="$(stat -c '%G' -- "$WORKSPACE_ROOT")"

[[ "$SERVICE_USER_ID" != "0" && "$SERVICE_USER" != "root" ]] || \
  bootstrap_die "Workspace root must be owned by a non-root service user: $WORKSPACE_ROOT"

readonly CONFIGURED_RUST_SERVER_PATH="$(read_compose_env_value 'RUST_SERVER_PATH')" || \
  bootstrap_die "RUST_SERVER_PATH is missing from canonical configuration: $RUST_SERVER_ENV_FILE"
[[ -n "$CONFIGURED_RUST_SERVER_PATH" ]] || \
  bootstrap_die "RUST_SERVER_PATH is empty in canonical configuration: $RUST_SERVER_ENV_FILE"
[[ "$CONFIGURED_RUST_SERVER_PATH" == /* ]] || \
  bootstrap_die "RUST_SERVER_PATH must be an absolute path for $SERVICE_NAME: $CONFIGURED_RUST_SERVER_PATH"
readonly RUST_SERVER_PATH="$(readlink -f -- "$CONFIGURED_RUST_SERVER_PATH")"
[[ -d "$RUST_SERVER_PATH" ]] || \
  bootstrap_die "Configured Rust server root is missing: $RUST_SERVER_PATH"
run_as_service_user test -r "$RUST_SERVER_PATH" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot read Rust server root: $RUST_SERVER_PATH"
run_as_service_user test -w "$RUST_SERVER_PATH" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot write Rust server root: $RUST_SERVER_PATH"
run_as_service_user test -x "$RUST_SERVER_PATH" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot traverse Rust server root: $RUST_SERVER_PATH"

[[ ! -L "$PLUGINS_STATE_ROOT" ]] || \
  bootstrap_die "Plugins state root must not be a symbolic link: $PLUGINS_STATE_ROOT"
if [[ -e "$PLUGINS_STATE_ROOT" && ! -d "$PLUGINS_STATE_ROOT" ]]; then
  bootstrap_die "Plugins state root must be a directory: $PLUGINS_STATE_ROOT"
fi

run_as_root install -d -o "$SERVICE_USER" -g "$SERVICE_GROUP" -m 0750 -- "$PLUGINS_STATE_ROOT" || \
  bootstrap_die "Failed to create Plugins state root: $PLUGINS_STATE_ROOT"
run_as_service_user test -r "$PLUGINS_STATE_ROOT" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot read Plugins state root: $PLUGINS_STATE_ROOT"
run_as_service_user test -w "$PLUGINS_STATE_ROOT" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot write Plugins state root: $PLUGINS_STATE_ROOT"
run_as_service_user test -x "$PLUGINS_STATE_ROOT" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot traverse Plugins state root: $PLUGINS_STATE_ROOT"

# PluginManagement creates its own Git workspaces at service startup.
# Bootstrap supplies only paths and the service identity; it never probes
# or modifies source checkouts.
run_as_service_user test -r "$WORKSPACE_ROOT" ||
  bootstrap_die "Plugins service user cannot read workspace root: $WORKSPACE_ROOT"
run_as_service_user test -w "$WORKSPACE_ROOT" ||
  bootstrap_die "Plugins service user cannot create Git workspaces under: $WORKSPACE_ROOT"
run_as_service_user test -x "$WORKSPACE_ROOT" ||
  bootstrap_die "Plugins service user cannot traverse workspace root: $WORKSPACE_ROOT"

provision_plugins_github_token

readonly DOTNET_EXECUTABLE="$(readlink -f -- "$(command -v dotnet)")"
[[ -x "$DOTNET_EXECUTABLE" ]] || \
  bootstrap_die "Resolved dotnet executable is not executable: $DOTNET_EXECUTABLE"

run_as_service_user test -r "$PLUGINS_DLL" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot read required Plugins DLL: $PLUGINS_DLL"
run_as_service_user test -x "$PLUGINS_RELEASE_DIR" || \
  bootstrap_die "Workspace owner $SERVICE_USER cannot traverse required Plugins release directory: $PLUGINS_RELEASE_DIR"
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

if ! wait_for_plugins_service_response; then
  run_as_root systemctl status "$SERVICE_NAME" --no-pager || true
  bootstrap_die "$SERVICE_NAME did not respond at $PLUGINS_PROBE_URL"
fi

bootstrap_info "$SERVICE_NAME is active; the host responds at $PLUGINS_PROBE_URL"
