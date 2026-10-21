#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly SUPPORTED_OS_ID="ubuntu"
readonly SUPPORTED_OS_VERSION_ID="24.04"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

if [[ ! -r /etc/os-release ]]; then
  bootstrap_die "Unsupported host: /etc/os-release is unavailable. Ubuntu $SUPPORTED_OS_VERSION_ID is required."
fi

# shellcheck disable=SC1091
source /etc/os-release
if [[ "${ID:-}" != "$SUPPORTED_OS_ID" || "${VERSION_ID:-}" != "$SUPPORTED_OS_VERSION_ID" ]]; then
  bootstrap_die "Unsupported host: Ubuntu $SUPPORTED_OS_VERSION_ID is required (detected ${PRETTY_NAME:-unknown})."
fi

for required_command in bash git ssh sudo; do
  require_command "$required_command"
done

if ! git -C "$PLATFORM_DIR" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  bootstrap_die "Platform directory is not a Git worktree: $PLATFORM_DIR"
fi

platform_origin="$(git -C "$PLATFORM_DIR" remote get-url origin 2>/dev/null || true)"
if [[ -z "$platform_origin" ]]; then
  bootstrap_die "Platform repository has no origin remote: $PLATFORM_DIR"
fi

bootstrap_info "Validating sudo access."
ensure_sudo_access
