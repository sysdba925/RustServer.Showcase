#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly SUPPORTED_OS_ID="ubuntu"
readonly SUPPORTED_OS_VERSION_ID="24.04"
readonly LAZYGIT_RELEASE_REPOSITORY="jesseduffield/lazygit"
readonly LAZYDOCKER_RELEASE_REPOSITORY="jesseduffield/lazydocker"
readonly TERMIDE_RELEASE_REPOSITORY="termide/termide"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

readonly -a BASE_APT_PACKAGES=(
  ca-certificates
  curl
  file
  gnupg
  gzip
  jq
  less
  mc
  openssl
  ripgrep
  tar
  unzip
  wget
  bat
)

readonly -a DOCKER_PACKAGES=(
  containerd.io
  docker-buildx-plugin
  docker-ce
  docker-ce-cli
  docker-compose-plugin
)

readonly -a CONFLICTING_DOCKER_PACKAGES=(
  containerd
  docker-compose
  docker-compose-v2
  docker-doc
  docker.io
  podman-docker
  runc
)

declare -a TEMP_DIRECTORIES=()

create_temporary_directory() {
  local variable_name="$1"
  local directory
  directory="$(mktemp -d -t rust-server-platform-bootstrap.XXXXXX)"
  TEMP_DIRECTORIES+=("$directory")
  printf -v "$variable_name" '%s' "$directory"
}

cleanup_temporary_directories() {
  local exit_code=$?
  local directory

  trap - EXIT
  for directory in "${TEMP_DIRECTORIES[@]}"; do
    if [[ -d "$directory" ]]; then
      rm -rf -- "$directory" || true
    fi
  done

  exit "$exit_code"
}
trap cleanup_temporary_directories EXIT

run_as_root() {
  if (( EUID == 0 )); then
    "$@"
  else
    sudo "$@"
  fi
}

apt_install() {
  run_as_root env DEBIAN_FRONTEND=noninteractive apt-get install -y "$@"
}

is_dpkg_package_installed() {
  local package_name="$1"

  [[ "$(dpkg-query -W -f='${db:Status-Status}' "$package_name" 2>/dev/null || true)" == "installed" ]]
}

verify_supported_host() {
  if [[ ! -r /etc/os-release ]]; then
    bootstrap_die "Unsupported host: /etc/os-release is unavailable. Ubuntu $SUPPORTED_OS_VERSION_ID is required."
  fi

  # shellcheck disable=SC1091
  source /etc/os-release
  if [[ "${ID:-}" != "$SUPPORTED_OS_ID" || "${VERSION_ID:-}" != "$SUPPORTED_OS_VERSION_ID" ]]; then
    bootstrap_die "Unsupported host: Ubuntu $SUPPORTED_OS_VERSION_ID is required (detected ${PRETTY_NAME:-unknown})."
  fi
}

install_base_packages() {
  bootstrap_info "Installing supporting Ubuntu packages."
  run_as_root apt-get update
  apt_install "${BASE_APT_PACKAGES[@]}"
}

dotnet_sdk_10_is_available() {
  command -v dotnet >/dev/null 2>&1 && \
    dotnet --list-sdks | awk '$1 ~ /^10\./ { found = 1 } END { exit !found }'
}

configure_microsoft_package_source() {
  if is_dpkg_package_installed "packages-microsoft-prod"; then
    bootstrap_skip "Microsoft package source is already installed."
    return
  fi

  local temporary_directory package_path
  create_temporary_directory temporary_directory
  package_path="$temporary_directory/packages-microsoft-prod.deb"

  bootstrap_info "Adding the official Microsoft package source."
  curl --fail --location --proto '=https' --retry 3 --retry-all-errors --tlsv1.2 \
    "https://packages.microsoft.com/config/$SUPPORTED_OS_ID/$SUPPORTED_OS_VERSION_ID/packages-microsoft-prod.deb" \
    --output "$package_path"
  run_as_root dpkg -i "$package_path"
}

ensure_dotnet_sdk() {
  if dotnet_sdk_10_is_available; then
    bootstrap_skip ".NET SDK 10 is already available."
    return
  fi

  configure_microsoft_package_source
  run_as_root apt-get update
  bootstrap_info "Installing .NET SDK 10."
  apt_install dotnet-sdk-10.0

  dotnet_sdk_10_is_available || bootstrap_die ".NET SDK 10 was not available after installation."
}

docker_packages_are_installed() {
  local package_name
  for package_name in "${DOCKER_PACKAGES[@]}"; do
    is_dpkg_package_installed "$package_name" || return 1
  done
}

reject_conflicting_docker_packages() {
  local package_name
  local -a conflicts=()

  for package_name in "${CONFLICTING_DOCKER_PACKAGES[@]}"; do
    if is_dpkg_package_installed "$package_name"; then
      conflicts+=("$package_name")
    fi
  done

  if ((${#conflicts[@]} > 0)); then
    bootstrap_die "Conflicting Docker packages are installed: ${conflicts[*]}. Remove them explicitly, then rerun bootstrap; no existing Docker packages were changed."
  fi
}

configure_docker_package_source() {
  local architecture codename temporary_directory key_path source_path
  architecture="$(dpkg --print-architecture)"
  codename="$(. /etc/os-release && printf '%s' "${UBUNTU_CODENAME:-$VERSION_CODENAME}")"
  create_temporary_directory temporary_directory
  key_path="$temporary_directory/docker.asc"
  source_path="$temporary_directory/docker.sources"

  if ! run_as_root test -s /etc/apt/keyrings/docker.asc; then
    bootstrap_info "Adding the official Docker signing key."
    curl --fail --location --proto '=https' --retry 3 --retry-all-errors --tlsv1.2 \
      https://download.docker.com/linux/ubuntu/gpg --output "$key_path"
    gpg --show-keys "$key_path" >/dev/null
    run_as_root install -D -m 0644 "$key_path" /etc/apt/keyrings/docker.asc
  else
    bootstrap_skip "Docker signing key is already present."
  fi

  cat >"$source_path" <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: $codename
Components: stable
Architectures: $architecture
Signed-By: /etc/apt/keyrings/docker.asc
EOF

  if ! run_as_root test -f /etc/apt/sources.list.d/docker.sources || \
    ! run_as_root cmp -s "$source_path" /etc/apt/sources.list.d/docker.sources; then
    bootstrap_info "Configuring the official Docker package source."
    run_as_root install -D -m 0644 "$source_path" /etc/apt/sources.list.d/docker.sources
  else
    bootstrap_skip "Docker package source is already configured."
  fi
}

ensure_docker_engine() {
  if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    bootstrap_skip "Docker Engine and Compose are already available."
  else
    reject_conflicting_docker_packages
    configure_docker_package_source
    run_as_root apt-get update
    bootstrap_info "Installing official Docker Engine and Compose plugin."
    apt_install "${DOCKER_PACKAGES[@]}"
  fi

  if [[ -d /run/systemd/system ]]; then
    run_as_root systemctl enable --now docker
    run_as_root systemctl is-active --quiet docker || \
      bootstrap_die "Docker service is not active."
  else
    bootstrap_warn "systemd is unavailable; Docker must be started by the host environment."
  fi

  docker --version >/dev/null || bootstrap_die "Docker CLI is unavailable."
  docker compose version >/dev/null || bootstrap_die "Docker Compose is unavailable."
}

ensure_docker_group_membership() {
  local bootstrap_user
  bootstrap_user="${SUDO_USER:-$(id -un)}"

  if [[ "$bootstrap_user" == "root" ]]; then
    return
  fi

  if id -nG "$bootstrap_user" | tr ' ' '\n' | grep -Fxq docker; then
    bootstrap_skip "User $bootstrap_user is already in the docker group."
    return
  fi

  run_as_root usermod -aG docker "$bootstrap_user"
  bootstrap_warn "Added $bootstrap_user to the docker group. Start a new login session before using Docker without sudo."
}

github_release_asset_metadata() {
  local repository="$1"
  local asset_name_pattern="$2"

  curl --fail --location --proto '=https' --retry 3 --retry-all-errors --tlsv1.2 \
    --header 'Accept: application/vnd.github+json' \
    --header 'X-GitHub-Api-Version: 2022-11-28' \
    "https://api.github.com/repos/$repository/releases/latest" | \
    jq -er --arg pattern "$asset_name_pattern" '
      [.assets[] | select(.name | test($pattern)) | {
        name: .name,
        url: .browser_download_url,
        digest: (.digest // "")
      }]
      | .[0] // empty
      | [.name, .url, .digest] | @tsv
    '
}

download_release_asset() {
  local repository="$1"
  local asset_name_pattern="$2"
  local destination_path="$3"
  local metadata asset_name asset_url asset_digest expected_digest

  if ! metadata="$(github_release_asset_metadata "$repository" "$asset_name_pattern")"; then
    bootstrap_die "Could not find a supported release asset for $repository."
  fi

  IFS=$'\t' read -r asset_name asset_url asset_digest <<<"$metadata"
  if [[ -z "$asset_name" || -z "$asset_url" || "$asset_digest" != sha256:* ]]; then
    bootstrap_die "Release metadata for $repository is incomplete or does not provide a SHA-256 digest."
  fi

  bootstrap_info "Downloading the official $repository release asset: $asset_name"
  curl --fail --location --proto '=https' --retry 3 --retry-all-errors --tlsv1.2 \
    "$asset_url" --output "$destination_path"

  expected_digest="${asset_digest#sha256:}"
  if ! printf '%s  %s\n' "$expected_digest" "$destination_path" | sha256sum --check --status -; then
    bootstrap_die "SHA-256 verification failed for $asset_name."
  fi
}

release_architecture() {
  case "$(uname -m)" in
    x86_64|amd64)
      printf '%s\n' "x86_64"
      ;;
    aarch64|arm64)
      printf '%s\n' "arm64"
      ;;
    *)
      bootstrap_die "Unsupported CPU architecture for release-installed tools: $(uname -m)"
      ;;
  esac
}

install_release_tarball_binary() {
  local command_name="$1"
  local repository="$2"
  local asset_name_pattern="$3"
  local temporary_directory archive_path binary_path

  if command -v "$command_name" >/dev/null 2>&1; then
    bootstrap_skip "$command_name is already available."
    return
  fi

  create_temporary_directory temporary_directory
  archive_path="$temporary_directory/$command_name.tar.gz"
  download_release_asset "$repository" "$asset_name_pattern" "$archive_path"

  if tar -tzf "$archive_path" | grep -Eq '(^/|(^|/)\.\.(/|$))'; then
    bootstrap_die "Refusing to extract an unsafe archive for $command_name."
  fi

  tar -xzf "$archive_path" -C "$temporary_directory"
  binary_path="$(find "$temporary_directory" -type f -name "$command_name" -print -quit)"
  [[ -n "$binary_path" ]] || bootstrap_die "Release archive for $command_name did not contain the expected executable."

  run_as_root install -D -m 0755 "$binary_path" "/usr/local/bin/$command_name"
}

ensure_terminal_tools() {
  local architecture lazygit_arch lazydocker_arch termide_arch

  [[ -n "$TERMIDE_RELEASE_REPOSITORY" ]] || \
    bootstrap_die "TERMIDE_RELEASE_REPOSITORY must be configured in this step"
  architecture="$(release_architecture)"

  lazygit_arch="$architecture"
  lazydocker_arch="$architecture"
  case "$architecture" in
    x86_64)
      termide_arch="x86_64"
      ;;
    arm64)
      termide_arch="aarch64"
      ;;
  esac

  install_release_tarball_binary \
    lazygit \
    "$LAZYGIT_RELEASE_REPOSITORY" \
    "^lazygit_[^/]+_linux_${lazygit_arch}\\.tar\\.gz$"
  install_release_tarball_binary \
    lazydocker \
    "$LAZYDOCKER_RELEASE_REPOSITORY" \
    "^lazydocker_[^/]+_Linux_${lazydocker_arch}\\.tar\\.gz$"
  install_release_tarball_binary \
    termide \
    "$TERMIDE_RELEASE_REPOSITORY" \
    "^termide-[^/]+-${termide_arch}-unknown-linux-gnu\\.tar\\.gz$"
}

ensure_bat_command() {
  require_command batcat

  if command -v bat >/dev/null 2>&1; then
    bootstrap_skip "bat command is already available."
    return
  fi

  run_as_root install -d -m 0755 /usr/local/bin
  run_as_root ln -s /usr/bin/batcat /usr/local/bin/bat
  require_command bat
}

verify_development_tools() {
  local required_command

  dotnet_sdk_10_is_available || bootstrap_die ".NET SDK 10 verification failed."

  for required_command in docker lazygit lazydocker termide mc less bat jq rg file; do
    require_command "$required_command"
  done

  dotnet --info >/dev/null
  docker --version >/dev/null
  docker compose version >/dev/null
  lazygit --version >/dev/null
  lazydocker --version >/dev/null
  termide --version >/dev/null
  mc --version >/dev/null
  less --version >/dev/null
  bat --version >/dev/null
  jq --version >/dev/null
  rg --version >/dev/null
  file --version >/dev/null
}

verify_supported_host
ensure_sudo_access
install_base_packages
ensure_dotnet_sdk
ensure_docker_engine
ensure_docker_group_membership
ensure_terminal_tools
ensure_bat_command
verify_development_tools

bootstrap_info "Development tools are installed and verified."
