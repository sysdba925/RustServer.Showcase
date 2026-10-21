#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly WEB_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Web"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"
# shellcheck source=../lib/topology.sh
source "$STEP_DIR/../lib/topology.sh"

readonly WEB_PACKAGE_JSON="$WEB_REPOSITORY_DIR/package.json"
readonly WEB_PACKAGE_LOCK="$WEB_REPOSITORY_DIR/package-lock.json"

require_command node
require_command npm
require_regular_file "$WEB_PACKAGE_JSON"
require_regular_file "$WEB_PACKAGE_LOCK"
load_topology_environment

bootstrap_info "Installing locked RustServer.Web dependencies."
(
  cd -- "$WEB_REPOSITORY_DIR"
  npm ci
)

bootstrap_info "Validating RustServer.Web."
(
  cd -- "$WEB_REPOSITORY_DIR"
  npm run check
)

bootstrap_info "RustServer.Web dependencies and validation completed successfully."
