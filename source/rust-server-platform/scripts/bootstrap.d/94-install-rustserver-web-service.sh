#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly WEB_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Web"
readonly WEB_PACKAGE_JSON="$WEB_REPOSITORY_DIR/package.json"
readonly WEB_VITE_BINARY="$WEB_REPOSITORY_DIR/node_modules/.bin/vite"
readonly NPM_EXECUTABLE="/usr/bin/npm"
readonly SERVICE_NAME="rustserver-web.service"
readonly SERVICE_UNIT_PATH="/etc/systemd/system/$SERVICE_NAME"
readonly WEB_READINESS_ATTEMPTS=30
readonly WEB_DASHBOARD_PATH="/dashboard/"
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

escape_systemd_path() {
  local path="$1"

  path="${path//%/%%}"
  path="${path//\\/\\\\}"
  path="${path// /\\x20}"
  path="${path//$'\t'/\\t}"
  path="${path//$'\n'/\\n}"
  printf '%s' "$path"
}

web_dashboard_url() {
  local web_url="${1%/}"

  printf '%s%s' "$web_url" "$WEB_DASHBOARD_PATH"
}

write_service_unit() {
  local unit_path="$1"

  {
    printf '%s\n' '[Unit]'
    printf '%s\n' 'Description=RustServer.Web Vite development server'
    printf '%s\n' 'Wants=network-online.target'
    printf '%s\n' 'After=network-online.target'
    printf '\n[Service]\n'
    printf '%s\n' 'Type=exec'
    printf 'User=%s\n' "$SERVICE_USER"
    printf 'WorkingDirectory=%s\n' "$(escape_systemd_path "$WEB_REPOSITORY_DIR")"
    printf 'EnvironmentFile=%s\n' "$RUSTSERVER_TOPOLOGY_FILE"
    printf '%s\n' 'ExecStart=/usr/bin/npm run dev'
    printf '%s\n' 'Restart=on-failure'
    printf '%s\n' 'RestartSec=5s'
    printf '%s\n' 'StandardOutput=journal'
    printf '%s\n' 'StandardError=journal'
    printf '%s\n' 'SyslogIdentifier=rustserver-web'
    printf '\n[Install]\n'
    printf '%s\n' 'WantedBy=multi-user.target'
  } > "$unit_path"
}

wait_for_web_readiness() {
  local attempt
  local response_status

  for ((attempt = 1; attempt <= WEB_READINESS_ATTEMPTS; attempt++)); do
    # Do not follow redirects: only a direct 200 from the dashboard entry point
    # proves Vite is serving the configured application path.
    if response_status="$(curl --fail --silent --show-error --output /dev/null --max-time 5 --write-out '%{http_code}' "$WEB_DASHBOARD_URL")" && \
      [[ "$response_status" == "200" ]]; then
      return 0
    fi

    sleep 1
  done

  return 1
}

main() {
  for required_command in cmp curl id install mktemp runuser sleep stat systemctl; do
    require_command "$required_command"
  done
  require_command node
  require_command npm

  [[ -d /run/systemd/system ]] || \
    bootstrap_die "systemd is unavailable; cannot install $SERVICE_NAME"

  ensure_sudo_access
  load_topology_environment
  readonly WEB_DASHBOARD_URL="$(web_dashboard_url "$RUSTSERVER_WEB_URL")"
  require_regular_file "$WEB_PACKAGE_JSON"
  [[ -x "$WEB_VITE_BINARY" ]] || \
    bootstrap_die "RustServer.Web dependencies are not prepared: $WEB_VITE_BINARY. Step 92 must run npm ci before $SERVICE_NAME is installed."
  [[ -x "$NPM_EXECUTABLE" ]] || \
    bootstrap_die "Required npm executable is not available at $NPM_EXECUTABLE"

  readonly SERVICE_USER="$(stat -c '%U' -- "$WORKSPACE_ROOT")"
  readonly SERVICE_USER_ID="$(stat -c '%u' -- "$WORKSPACE_ROOT")"

  [[ "$SERVICE_USER_ID" != "0" && "$SERVICE_USER" != "root" ]] || \
    bootstrap_die "Workspace root must be owned by a non-root service user: $WORKSPACE_ROOT"
  id "$SERVICE_USER" >/dev/null 2>&1 || \
    bootstrap_die "Workspace owner is not a local user: $SERVICE_USER"

  for required_readable_path in "$WEB_PACKAGE_JSON" "$WEB_VITE_BINARY"; do
    run_as_service_user test -r "$required_readable_path" || \
      bootstrap_die "Workspace owner $SERVICE_USER cannot read required path: $required_readable_path"
  done
  run_as_service_user test -x "$WEB_VITE_BINARY" || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot execute Vite: $WEB_VITE_BINARY"
  run_as_service_user test -x "$NPM_EXECUTABLE" || \
    bootstrap_die "Workspace owner $SERVICE_USER cannot execute npm: $NPM_EXECUTABLE"

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

  if ! wait_for_web_readiness; then
    run_as_root systemctl status "$SERVICE_NAME" --no-pager || true
    bootstrap_die "$SERVICE_NAME did not become ready at $WEB_DASHBOARD_URL"
  fi

  bootstrap_info "$SERVICE_NAME is active; Dashboard is available at $WEB_DASHBOARD_URL"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
