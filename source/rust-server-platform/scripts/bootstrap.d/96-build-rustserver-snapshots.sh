#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly SNAPSHOTS_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Snapshots"
readonly SNAPSHOTS_SOLUTION="$SNAPSHOTS_REPOSITORY_DIR/RustServer.Snapshots.sln"
readonly NUGET_GLOBAL_PACKAGES_DIR="$WORKSPACE_ROOT/packages/global-packages"
readonly NUGET_CONFIG_PATH="$WORKSPACE_ROOT/NuGet.config"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

require_command dotnet
require_regular_file "$SNAPSHOTS_SOLUTION"
require_regular_file "$NUGET_CONFIG_PATH"
[[ -d "$NUGET_GLOBAL_PACKAGES_DIR" && ! -L "$NUGET_GLOBAL_PACKAGES_DIR" ]] || \
  bootstrap_die "Workspace NuGet global-packages directory is not a regular directory: $NUGET_GLOBAL_PACKAGES_DIR"

export NUGET_PACKAGES="$NUGET_GLOBAL_PACKAGES_DIR"

bootstrap_info "Restoring RustServer.Snapshots"
dotnet restore "$SNAPSHOTS_SOLUTION" --force --configfile "$NUGET_CONFIG_PATH"

bootstrap_info "Building RustServer.Snapshots (Release)"
dotnet build "$SNAPSHOTS_SOLUTION" --configuration Release --no-restore -p:TreatWarningsAsErrors=true

bootstrap_info "Testing RustServer.Snapshots (Release)"
dotnet test "$SNAPSHOTS_SOLUTION" --configuration Release --no-build --no-restore

bootstrap_info "Verifying RustServer.Snapshots OpenAPI contract"
(
  cd -- "$SNAPSHOTS_REPOSITORY_DIR"
  dotnet run --project tools/RustServer.Snapshots.ApiContractGenerator \
    --configuration Release --no-build --no-restore -- --verify
)
