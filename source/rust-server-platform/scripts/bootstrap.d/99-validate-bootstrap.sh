#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

require_dotnet_sdk_10() {
  dotnet --list-sdks | awk '$1 ~ /^10\./ { found = 1 } END { exit !found }' || \
    bootstrap_die "Required .NET SDK 10 is not available"
}

validate_docker_engine() {
  if docker version --format '{{.Server.Version}}' >/dev/null; then
    :
  elif sudo docker version --format '{{.Server.Version}}' >/dev/null; then
    bootstrap_warn "Docker Engine is available through sudo; start a new login session before using Docker without sudo."
  else
    bootstrap_die "Docker Engine is not available to the current user or through sudo"
  fi

  docker compose version >/dev/null || \
    bootstrap_die "Docker Compose plugin is not available"
}

for command_name in dotnet docker lazygit lazydocker termide mc less bat; do
  require_command "$command_name"
done

require_dotnet_sdk_10
validate_docker_engine

for command_name in lazygit lazydocker termide mc less bat; do
  "$command_name" --version >/dev/null || \
    bootstrap_die "Tool did not pass its version check: $command_name"
done

bootstrap_info "All step-specific validations completed successfully"
