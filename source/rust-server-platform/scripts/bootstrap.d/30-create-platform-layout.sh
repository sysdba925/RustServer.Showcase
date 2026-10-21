#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly REPOS_DIR="$WORKSPACE_ROOT/repos"
readonly NUGET_DIR="$WORKSPACE_ROOT/packages/nuget"
readonly NUGET_GLOBAL_PACKAGES_DIR="$WORKSPACE_ROOT/packages/global-packages"
readonly RUST_SERVER_DIR="/srv/rust/server"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

for directory in "$REPOS_DIR" "$WORKSPACE_ROOT/packages" "$NUGET_DIR" "$NUGET_GLOBAL_PACKAGES_DIR"; do
  if [[ -d "$directory" ]]; then
    bootstrap_skip "Directory already exists: $directory"
  else
    mkdir -p -- "$directory"
    bootstrap_info "Created directory: $directory"
  fi
done

if [[ -d "$RUST_SERVER_DIR" ]]; then
  bootstrap_skip "Rust server runtime directory already exists: $RUST_SERVER_DIR"
else
  sudo install -d -m 0755 -- "$RUST_SERVER_DIR"
  bootstrap_info "Created Rust server runtime directory: $RUST_SERVER_DIR"
fi
