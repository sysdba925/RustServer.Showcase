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

health_url() {
  updater_health_url "$UPDATER_ENVIRONMENT_FILE"
}

verify_health_endpoint() {
  verify_updater_health "$1"
}

ensure_sudo_access
dotnet_has_aspnetcore_runtime_10 || \
  bootstrap_die "ASP.NET Core Runtime 10 is not installed"
updater_current_directory >/dev/null
require_root_regular_file "$UPDATER_ENVIRONMENT_FILE"
run_as_root systemctl is-active --quiet rustserver-updater.service || \
  bootstrap_die "rustserver-updater.service is not active"

verify_health_endpoint "$(health_url)"
bootstrap_info "Existing Updater runtime configuration was preserved on this run"
