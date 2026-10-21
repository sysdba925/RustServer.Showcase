#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly CONTROL_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Control"
readonly NUGET_DIR="$WORKSPACE_ROOT/packages/nuget"
readonly NUGET_GLOBAL_PACKAGES_DIR="$WORKSPACE_ROOT/packages/global-packages"
readonly NUGET_CONFIG_PATH="$WORKSPACE_ROOT/NuGet.config"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

readonly CONTROL_SOLUTION="$CONTROL_REPOSITORY_DIR/RustServer.Control.sln"

read_nuget_config_value() {
  local key="$1"

  awk -v key="$key" '
    function attribute_value(line, attribute, pattern, value) {
      pattern = attribute "[[:space:]]*=[[:space:]]*\"[^\"]*\""
      if (!match(line, pattern)) return ""
      value = substr(line, RSTART, RLENGTH)
      sub("^[^=]*=[[:space:]]*\"", "", value)
      sub("\"$", "", value)
      return value
    }
    /<add[[:space:]]/ && attribute_value($0, "key") == key {
      value = attribute_value($0, "value")
      if (value != "") found = value
    }
    END {
      if (found != "") print found
      else exit 1
    }
  ' "$NUGET_CONFIG_PATH"
}

use_workspace_nuget_cache() {
  [[ -d "$NUGET_GLOBAL_PACKAGES_DIR" && ! -L "$NUGET_GLOBAL_PACKAGES_DIR" ]] || \
    bootstrap_die "Workspace NuGet global-packages directory is not a regular directory: $NUGET_GLOBAL_PACKAGES_DIR"
  export NUGET_PACKAGES="$NUGET_GLOBAL_PACKAGES_DIR"
}

validate_workspace_nuget_config() {
  local configured_sources
  local physical_nuget_dir
  local configured_global_packages_dir
  local physical_global_packages_dir

  [[ -f "$NUGET_CONFIG_PATH" ]] || bootstrap_die "Workspace NuGet configuration is missing: $NUGET_CONFIG_PATH"
  physical_nuget_dir="$(cd -- "$NUGET_DIR" && pwd -P)"
  physical_global_packages_dir="$(cd -- "$NUGET_GLOBAL_PACKAGES_DIR" && pwd -P)"
  configured_sources="$(DOTNET_CLI_UI_LANGUAGE=en dotnet nuget list source \
  --configfile "$NUGET_CONFIG_PATH" \
  --format Detailed)"

  if ! has_enabled_nuget_source "$configured_sources" "https://api.nuget.org/v3/index.json"; then
    bootstrap_die "Workspace NuGet configuration must enable nuget.org: $NUGET_CONFIG_PATH"
  fi

  if ! has_enabled_nuget_source "$configured_sources" "$physical_nuget_dir"; then
    bootstrap_die "Workspace NuGet configuration must enable the local feed: $NUGET_DIR"
  fi

  configured_global_packages_dir="$(read_nuget_config_value "globalPackagesFolder")" || \
    bootstrap_die "Workspace NuGet configuration must define globalPackagesFolder: $NUGET_CONFIG_PATH"
  [[ "$configured_global_packages_dir" != /* && "$configured_global_packages_dir" != [A-Za-z]:* ]] || \
    bootstrap_die "Workspace NuGet globalPackagesFolder must be a relative path: $NUGET_CONFIG_PATH"

  configured_global_packages_dir="$(cd -- "$WORKSPACE_ROOT/$configured_global_packages_dir" && pwd -P)" || \
    bootstrap_die "Workspace NuGet globalPackagesFolder does not resolve to a directory: $NUGET_CONFIG_PATH"
  [[ "$configured_global_packages_dir" == "$physical_global_packages_dir" ]] || \
    bootstrap_die "Workspace NuGet globalPackagesFolder must resolve to: $NUGET_GLOBAL_PACKAGES_DIR"
}

has_enabled_nuget_source() {
  local configured_sources="$1"
  local source_value="$2"

  awk -v source_value="$source_value" '
    /^[[:space:]]*[0-9]+\.[[:space:]].*\[(Enabled|Disabled)\][[:space:]]*$/ {
      source_is_enabled = ($0 ~ /\[Enabled\][[:space:]]*$/)
      next
    }
    source_is_enabled && index($0, source_value) {
      found = 1
    }
    END {
      exit(found ? 0 : 1)
    }
  ' <<<"$configured_sources"
}

require_command dotnet
require_regular_file "$CONTROL_SOLUTION"
[[ -d "$NUGET_DIR" ]] || bootstrap_die "Local NuGet feed directory is missing: $NUGET_DIR"

validate_workspace_nuget_config
use_workspace_nuget_cache

bootstrap_info "Restoring RustServer.Control"
dotnet restore "$CONTROL_SOLUTION" --force --configfile "$NUGET_CONFIG_PATH"

bootstrap_info "Building RustServer.Control (Release)"
dotnet build "$CONTROL_SOLUTION" --configuration Release --no-restore -p:TreatWarningsAsErrors=true

bootstrap_info "Testing RustServer.Control (Release)"
dotnet test "$CONTROL_SOLUTION" --configuration Release --no-build --no-restore
