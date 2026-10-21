#!/usr/bin/env bash
set -Eeuo pipefail

STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly STEP_DIR
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

readonly SUPPORTED_OS_ID="ubuntu"
readonly SUPPORTED_OS_VERSION_ID="24.04"

[[ -r /etc/os-release ]] || \
  bootstrap_die "Unsupported host: /etc/os-release is unavailable; Ubuntu $SUPPORTED_OS_VERSION_ID is required"

# shellcheck disable=SC1091
source /etc/os-release
if [[ "${ID:-}" != "$SUPPORTED_OS_ID" || "${VERSION_ID:-}" != "$SUPPORTED_OS_VERSION_ID" ]]; then
  bootstrap_die "Unsupported host: Ubuntu $SUPPORTED_OS_VERSION_ID is required (detected ${PRETTY_NAME:-unknown})"
fi

[[ "$(uname -m)" == "x86_64" ]] || \
  bootstrap_die "Unsupported architecture: x86_64 is required (detected $(uname -m))"

for required_command in apt-get awk basename cat chmod chown cp curl dirname dpkg dpkg-deb dpkg-query find flock getent grep id install ln mktemp python3 readlink rm rmdir sha256sum sleep sort stat systemctl tar tee uname useradd; do
  require_command "$required_command"
done

ensure_sudo_access
bootstrap_info "Ubuntu $SUPPORTED_OS_VERSION_ID x64 prerequisites are available"
