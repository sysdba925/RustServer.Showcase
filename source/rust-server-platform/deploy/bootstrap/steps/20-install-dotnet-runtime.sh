#!/usr/bin/env bash
set -Eeuo pipefail

STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly STEP_DIR
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

if ! bootstrap_mode_is install; then
  bootstrap_skip "Step is not required for Bootstrap mode: $(bootstrap_mode)"
  exit 0
fi

readonly MICROSOFT_REPOSITORY_PACKAGE_URL="https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb"
readonly MICROSOFT_REPOSITORY_PACKAGE_NAME="packages-microsoft-prod"
readonly ASPNETCORE_RUNTIME_PACKAGE="aspnetcore-runtime-10.0"

install_microsoft_repository_if_missing() {
  local temporary_directory
  local repository_package

  if dpkg-query --show --showformat='${Status}' "$MICROSOFT_REPOSITORY_PACKAGE_NAME" 2>/dev/null | grep -qx 'install ok installed'; then
    bootstrap_skip "Microsoft package repository is already installed"
    return
  fi

  temporary_directory="$(mktemp -d)"
  trap 'rm -rf -- "${temporary_directory:-}"' EXIT
  repository_package="$temporary_directory/packages-microsoft-prod.deb"

  bootstrap_info "Installing the Microsoft package repository"
  curl --fail --location --silent --show-error --output "$repository_package" "$MICROSOFT_REPOSITORY_PACKAGE_URL" || \
    bootstrap_die "Could not download the Microsoft package repository configuration"
  [[ -s "$repository_package" ]] || \
    bootstrap_die "Microsoft package repository download is empty"
  dpkg-deb --field "$repository_package" Package >/dev/null 2>&1 || \
    bootstrap_die "Microsoft package repository download is not a Debian package"
  run_as_root dpkg --install "$repository_package" || \
    bootstrap_die "Could not install the Microsoft package repository configuration"
  rm -rf -- "$temporary_directory"
  trap - EXIT
}

ensure_sudo_access
if dotnet_has_aspnetcore_runtime_10; then
  bootstrap_skip "ASP.NET Core Runtime 10 is already installed"
  exit 0
fi

install_microsoft_repository_if_missing
bootstrap_info "Installing ASP.NET Core Runtime 10"
run_as_root apt-get update || bootstrap_die "Could not refresh APT package metadata"
run_as_root apt-get install --yes --no-install-recommends "$ASPNETCORE_RUNTIME_PACKAGE" || \
  bootstrap_die "Could not install ASP.NET Core Runtime 10"

dotnet_has_aspnetcore_runtime_10 || \
  bootstrap_die "ASP.NET Core Runtime 10 was not found after installation"
bootstrap_info "ASP.NET Core Runtime 10 is installed"
