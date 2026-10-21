#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly REPOSITORY_NAME="RustServer.Web"
readonly REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Web"
readonly REPOSITORY_URL="git@github.com:sysdba925/RustServer.Web.git"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

# shellcheck source=../lib/git.sh
source "$STEP_DIR/../lib/git.sh"

sync_git_repository \
  "$REPOSITORY_NAME" \
  "$REPOSITORY_DIR" \
  "$REPOSITORY_URL"
