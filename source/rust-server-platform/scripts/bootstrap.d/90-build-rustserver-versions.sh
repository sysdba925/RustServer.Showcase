#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly VERSIONS_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Versions"
readonly NUGET_GLOBAL_PACKAGES_DIR="$WORKSPACE_ROOT/packages/global-packages"
readonly NUGET_CONFIG_PATH="$WORKSPACE_ROOT/NuGet.config"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

readonly VERSIONS_SOLUTION="$VERSIONS_REPOSITORY_DIR/RustServer.Versions.sln"

require_command dotnet
require_regular_file "$VERSIONS_SOLUTION"
[[ -f "$NUGET_CONFIG_PATH" ]] || bootstrap_die "Workspace NuGet configuration is missing: $NUGET_CONFIG_PATH"
[[ -d "$NUGET_GLOBAL_PACKAGES_DIR" && ! -L "$NUGET_GLOBAL_PACKAGES_DIR" ]] || \
  bootstrap_die "Workspace NuGet global-packages directory is not a regular directory: $NUGET_GLOBAL_PACKAGES_DIR"
export NUGET_PACKAGES="$NUGET_GLOBAL_PACKAGES_DIR"

bootstrap_info "Restoring RustServer.Versions"
dotnet restore "$VERSIONS_SOLUTION" --configfile "$NUGET_CONFIG_PATH"

bootstrap_info "Building RustServer.Versions (Release)"
dotnet build "$VERSIONS_SOLUTION" --configuration Release --no-restore -p:TreatWarningsAsErrors=true

bootstrap_info "Testing RustServer.Versions (Release)"
dotnet test "$VERSIONS_SOLUTION" --configuration Release --no-build --no-restore
