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

ensure_sudo_access

# Parents are created explicitly so their ownership and permissions are also
# predictable on a clean VM, while existing application data is untouched.
ensure_root_directory "/opt/rustserver" "0755" "root" "root"
ensure_root_directory "/opt/rustserver/updater" "0755" "root" "root"
ensure_root_directory "/opt/rustserver/updater/releases" "0755" "root" "root"
ensure_root_directory "/opt/rustserver/web" "0755" "root" "root"
ensure_root_directory "/opt/rustserver/web/releases" "0755" "root" "root"
ensure_root_directory "/etc/rustserver" "0755" "root" "root"
ensure_root_directory "/var/lib/rustserver" "0755" "root" "root"
ensure_root_directory "/var/lib/rustserver/updater" "0750" "root" "root"
