#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly SCRIPTS_DIR="$(cd -- "$STEP_DIR/.." && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$SCRIPTS_DIR/.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly RCON_REPOSITORY_DIR="$WORKSPACE_ROOT/repos/RustServer.Rcon"
readonly NUGET_DIR="$WORKSPACE_ROOT/packages/nuget"
readonly NUGET_GLOBAL_PACKAGES_DIR="$WORKSPACE_ROOT/packages/global-packages"
readonly NUGET_CONFIG_PATH="$WORKSPACE_ROOT/NuGet.config"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

readonly NUGET_CONFIG_TEMPLATE="$SCRIPTS_DIR/bootstrap/NuGet.config.template"
readonly RCON_SOLUTION="$RCON_REPOSITORY_DIR/RustServer.Rcon.sln"
readonly RCON_PROJECT="$RCON_REPOSITORY_DIR/src/RustServer.Rcon/RustServer.Rcon.csproj"

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

ensure_workspace_nuget_config() {
  require_regular_file "$NUGET_CONFIG_TEMPLATE"

  if [[ -e "$NUGET_CONFIG_PATH" && ! -f "$NUGET_CONFIG_PATH" ]]; then
    bootstrap_die "Workspace NuGet configuration is not a regular file: $NUGET_CONFIG_PATH"
  fi

  if [[ -f "$NUGET_CONFIG_PATH" ]]; then
    bootstrap_skip "Preserving existing workspace NuGet configuration: $NUGET_CONFIG_PATH"
  else
    cp -- "$NUGET_CONFIG_TEMPLATE" "$NUGET_CONFIG_PATH"
    bootstrap_info "Created workspace NuGet configuration: $NUGET_CONFIG_PATH"
  fi
}

validate_workspace_nuget_config() {
  local configured_sources
  local physical_nuget_dir
  local configured_global_packages_dir
  local physical_global_packages_dir

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

read_rcon_package_metadata() {
  local metadata

  metadata="$(dotnet msbuild "$RCON_PROJECT" -nologo -getProperty:PackageId -getProperty:PackageVersion)"
  RCON_PACKAGE_ID="$(jq -er '.Properties.PackageId' <<<"$metadata")"
  RCON_PACKAGE_VERSION="$(jq -er '.Properties.PackageVersion' <<<"$metadata")"

  if [[ ! "$RCON_PACKAGE_ID" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
    bootstrap_die "RustServer.Rcon project resolved an unsafe PackageId: $RCON_PACKAGE_ID"
  fi

  if [[ ! "$RCON_PACKAGE_VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z.+-]*$ ]]; then
    bootstrap_die "RustServer.Rcon project resolved an unsafe PackageVersion: $RCON_PACKAGE_VERSION"
  fi
}

require_command dotnet
require_command jq
require_command unzip
require_regular_file "$RCON_SOLUTION"
require_regular_file "$RCON_PROJECT"
[[ -d "$NUGET_DIR" ]] || bootstrap_die "Local NuGet feed directory is missing: $NUGET_DIR"

ensure_workspace_nuget_config
validate_workspace_nuget_config
read_rcon_package_metadata
use_workspace_nuget_cache

readonly RCON_PACKAGE_PATH="$NUGET_DIR/$RCON_PACKAGE_ID.$RCON_PACKAGE_VERSION.nupkg"
readonly RCON_GLOBAL_PACKAGE_CACHE_DIR="$NUGET_GLOBAL_PACKAGES_DIR/${RCON_PACKAGE_ID,,}/${RCON_PACKAGE_VERSION,,}"
readonly RCON_PACK_DIRECTORY="$(mktemp -d "$NUGET_DIR/.rcon-pack.XXXXXX")"

cleanup_rcon_pack_directory() {
  rm -rf -- "$RCON_PACK_DIRECTORY"
}

trap cleanup_rcon_pack_directory EXIT

bootstrap_info "Restoring RustServer.Rcon"
dotnet restore "$RCON_SOLUTION" --configfile "$NUGET_CONFIG_PATH"

bootstrap_info "Building RustServer.Rcon (Release)"
dotnet build "$RCON_SOLUTION" --configuration Release --no-restore -p:TreatWarningsAsErrors=true

bootstrap_info "Testing RustServer.Rcon (Release)"
dotnet test "$RCON_SOLUTION" --configuration Release --no-build --no-restore

if [[ -e "$RCON_PACKAGE_PATH" || -L "$RCON_PACKAGE_PATH" ]]; then
  [[ -f "$RCON_PACKAGE_PATH" && ! -L "$RCON_PACKAGE_PATH" ]] || \
    bootstrap_die "Existing RustServer.Rcon package artifact is not a regular file: $RCON_PACKAGE_PATH"
fi

bootstrap_info "Packing RustServer.Rcon $RCON_PACKAGE_ID $RCON_PACKAGE_VERSION"
dotnet pack "$RCON_PROJECT" --configuration Release --no-build --no-restore --output "$RCON_PACK_DIRECTORY"

readonly FRESH_RCON_PACKAGE_PATH="$RCON_PACK_DIRECTORY/$RCON_PACKAGE_ID.$RCON_PACKAGE_VERSION.nupkg"
require_nonempty_file "$FRESH_RCON_PACKAGE_PATH"
unzip -tq "$FRESH_RCON_PACKAGE_PATH" >/dev/null

mv -f -- "$FRESH_RCON_PACKAGE_PATH" "$RCON_PACKAGE_PATH"
bootstrap_info "Verified RustServer.Rcon package artifact: $RCON_PACKAGE_PATH"

if [[ -e "$RCON_GLOBAL_PACKAGE_CACHE_DIR" || -L "$RCON_GLOBAL_PACKAGE_CACHE_DIR" ]]; then
  [[ -d "$RCON_GLOBAL_PACKAGE_CACHE_DIR" && ! -L "$RCON_GLOBAL_PACKAGE_CACHE_DIR" ]] || \
    bootstrap_die "RustServer.Rcon workspace cache entry is not a regular directory: $RCON_GLOBAL_PACKAGE_CACHE_DIR"

  rm -rf -- "$RCON_GLOBAL_PACKAGE_CACHE_DIR"
  bootstrap_info "Invalidated RustServer.Rcon workspace cache entry for $RCON_PACKAGE_ID $RCON_PACKAGE_VERSION"
else
  bootstrap_skip "No RustServer.Rcon workspace cache entry to invalidate for $RCON_PACKAGE_ID $RCON_PACKAGE_VERSION"
fi
