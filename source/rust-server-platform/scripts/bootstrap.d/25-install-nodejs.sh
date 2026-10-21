#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly NODE_MAJOR_VERSION=22
readonly NODE_MINIMUM_MINOR_VERSION=12
readonly NODESOURCE_KEY_URL="https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key"
readonly NODESOURCE_KEY_PATH="/etc/apt/keyrings/nodesource.gpg"
readonly NODESOURCE_SOURCE_PATH="/etc/apt/sources.list.d/nodesource.list"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

run_as_root() {
  if (( EUID == 0 )); then
    "$@"
  else
    sudo "$@"
  fi
}

node_version_is_supported() {
  local version major minor

  command -v node >/dev/null 2>&1 || return 1
  version="$(node -p 'process.versions.node' 2>/dev/null)" || return 1
  [[ "$version" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)(-[0-9A-Za-z.-]+)?$ ]] || return 1

  major="${BASH_REMATCH[1]}"
  minor="${BASH_REMATCH[2]}"
  (( 10#$major > NODE_MAJOR_VERSION || \
    (10#$major == NODE_MAJOR_VERSION && 10#$minor >= NODE_MINIMUM_MINOR_VERSION) ))
}

node_tools_are_usable() {
  node_version_is_supported && \
    command -v npm >/dev/null 2>&1 && npm --version >/dev/null && \
    command -v npx >/dev/null 2>&1 && npx --version >/dev/null
}

configure_nodesource_repository() (
  local temporary_directory key_path source_path

  temporary_directory="$(mktemp -d -t rust-server-platform-nodejs.XXXXXX)"
  trap 'rm -rf -- "$temporary_directory"' EXIT
  key_path="$temporary_directory/nodesource.gpg"
  source_path="$temporary_directory/nodesource.list"

  bootstrap_info "Configuring the NodeSource Node.js $NODE_MAJOR_VERSION repository."
  curl --fail --location --proto '=https' --retry 3 --retry-all-errors --tlsv1.2 \
    "$NODESOURCE_KEY_URL" | gpg --dearmor --yes --output "$key_path"
  [[ -s "$key_path" ]] || bootstrap_die "NodeSource signing key is empty."

  printf 'deb [signed-by=%s] https://deb.nodesource.com/node_%s.x nodistro main\n' \
    "$NODESOURCE_KEY_PATH" "$NODE_MAJOR_VERSION" >"$source_path"

  if ! run_as_root test -f "$NODESOURCE_KEY_PATH" || ! run_as_root cmp -s "$key_path" "$NODESOURCE_KEY_PATH"; then
    run_as_root install -D -m 0644 "$key_path" "$NODESOURCE_KEY_PATH"
  else
    bootstrap_skip "NodeSource signing key is already configured."
  fi

  if ! run_as_root test -f "$NODESOURCE_SOURCE_PATH" || ! run_as_root cmp -s "$source_path" "$NODESOURCE_SOURCE_PATH"; then
    run_as_root install -D -m 0644 "$source_path" "$NODESOURCE_SOURCE_PATH"
  else
    bootstrap_skip "NodeSource package source is already configured."
  fi
)

ensure_nodejs() {
  if node_tools_are_usable; then
    bootstrap_skip "Node.js $(node -p 'process.versions.node'), npm, and npx already satisfy the frontend requirement."
    return
  fi

  configure_nodesource_repository
  run_as_root apt-get update
  bootstrap_info "Installing Node.js $NODE_MAJOR_VERSION LTS."
  run_as_root env DEBIAN_FRONTEND=noninteractive apt-get install -y nodejs
}

ensure_sudo_access
require_command curl
require_command gpg
ensure_nodejs

node_tools_are_usable || bootstrap_die "Node.js $NODE_MAJOR_VERSION.$NODE_MINIMUM_MINOR_VERSION or newer, npm, and npx were not available after installation."
node --version
npm --version
npx --version
bootstrap_info "Node.js, npm, and npx are installed and verified."
