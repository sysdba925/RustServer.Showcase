#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly PLUGINS_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Plugins"
readonly PLUGINS_SOLUTION="$PLUGINS_REPOSITORY_DIR/RustServer.Plugins.sln"
readonly NUGET_GLOBAL_PACKAGES_DIR="$WORKSPACE_ROOT/packages/global-packages"
readonly NUGET_CONFIG_PATH="$WORKSPACE_ROOT/NuGet.config"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

require_command dotnet
require_regular_file "$PLUGINS_SOLUTION"
[[ -f "$NUGET_CONFIG_PATH" ]] || \
  bootstrap_die "Workspace NuGet configuration is missing: $NUGET_CONFIG_PATH"
[[ -d "$NUGET_GLOBAL_PACKAGES_DIR" && ! -L "$NUGET_GLOBAL_PACKAGES_DIR" ]] || \
  bootstrap_die "Workspace NuGet global-packages directory is not a regular directory: $NUGET_GLOBAL_PACKAGES_DIR"

export NUGET_PACKAGES="$NUGET_GLOBAL_PACKAGES_DIR"

bootstrap_info "Restoring RustServer.Plugins"
dotnet restore "$PLUGINS_SOLUTION" --configfile "$NUGET_CONFIG_PATH"

bootstrap_info "Building RustServer.Plugins (Release)"
dotnet build "$PLUGINS_SOLUTION" --configuration Release --no-restore -p:TreatWarningsAsErrors=true

bootstrap_info "Testing RustServer.Plugins (Release)"
dotnet test "$PLUGINS_SOLUTION" --configuration Release --no-build --no-restore

bootstrap_info "Verifying RustServer.Plugins OpenAPI contract"
(
  cd -- "$PLUGINS_REPOSITORY_DIR"
  dotnet run --project tools/RustServer.Plugins.ApiContractGenerator \
    --configuration Release --no-build --no-restore -- --verify
)
