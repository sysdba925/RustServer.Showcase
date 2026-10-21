#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly RCON_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Rcon"
readonly RCON_GRPC_PROJECT="$RCON_REPOSITORY_DIR/src/RustServer.Rcon.Grpc/RustServer.Rcon.Grpc.csproj"
readonly RCON_GRPC_RELEASE_DIR="$RCON_REPOSITORY_DIR/src/RustServer.Rcon.Grpc/bin/Release/net10.0"
readonly RCON_GRPC_DLL="$RCON_GRPC_RELEASE_DIR/RustServer.Rcon.Grpc.dll"
readonly NUGET_GLOBAL_PACKAGES_DIR="$WORKSPACE_ROOT/packages/global-packages"
readonly NUGET_CONFIG_PATH="$WORKSPACE_ROOT/NuGet.config"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

require_command dotnet
require_regular_file "$RCON_GRPC_PROJECT"
require_regular_file "$NUGET_CONFIG_PATH"
[[ -d "$NUGET_GLOBAL_PACKAGES_DIR" && ! -L "$NUGET_GLOBAL_PACKAGES_DIR" ]] || \
  bootstrap_die "Workspace NuGet global-packages directory is not a regular directory: $NUGET_GLOBAL_PACKAGES_DIR"

export NUGET_PACKAGES="$NUGET_GLOBAL_PACKAGES_DIR"

# Step 70 restores the solution before this step and owns the NuGet package
# build. Build the executable explicitly so its release artifact is owned and
# validated independently of the library package.
bootstrap_info "Building RustServer.Rcon.Grpc (Release)"
dotnet build "$RCON_GRPC_PROJECT" --configuration Release --no-restore -p:TreatWarningsAsErrors=true

require_nonempty_file "$RCON_GRPC_DLL"
require_nonempty_file "$RCON_GRPC_RELEASE_DIR/RustServer.Rcon.Grpc.deps.json"
require_nonempty_file "$RCON_GRPC_RELEASE_DIR/RustServer.Rcon.Grpc.runtimeconfig.json"

bootstrap_info "Verified RustServer.Rcon.Grpc release artifact: $RCON_GRPC_DLL"
