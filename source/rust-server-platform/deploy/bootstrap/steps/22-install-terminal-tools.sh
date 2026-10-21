#!/usr/bin/env bash
# One-time terminal toolkit owned by Production Bootstrap (Ubuntu 24.04 x64).
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly TOOLS_SOURCE="$STEP_DIR/../assets/tools"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

if ! bootstrap_mode_is install; then
  bootstrap_skip "Terminal tools are not needed for mode: $(bootstrap_mode)"
  exit 0
fi

# Production uses the root/sudo helper from deploy/bootstrap/lib/common.sh.
bootstrap_warn() {
  bootstrap_log "WARN" "$*" >&2
}

bootstrap_user="${SUDO_USER:-$(id -un)}"
bootstrap_home="$(getent passwd "$bootstrap_user" | cut -d: -f6)"
[[ -n "$bootstrap_home" && -d "$bootstrap_home" ]] || bootstrap_die "Cannot find home for $bootstrap_user"

# deploy/cleanup.sh retains packages, the tools command and the user's .bashrc.
# Fast path: do nothing at all when the first installation survived cleanup.
all_terminal_tools_installed() {
  local binary
  for binary in termide mc lazygit lazydocker bat rg jq micro btop tmux ncdu fzf duf lnav systemctl-tui lazyjournal yazi hx; do
    command -v "$binary" >/dev/null 2>&1 || return 1
  done
  # Dust release installer currently supports amd64 only.
  if [[ "$(dpkg --print-architecture)" == amd64 ]]; then
    command -v dust >/dev/null 2>&1 || return 1
  fi
  [[ -x "$bootstrap_home/.local/bin/posting" ]] || command -v posting >/dev/null 2>&1
}

if all_terminal_tools_installed &&
   [[ -x /usr/local/bin/tools ]] &&
   grep -Fqx '# >>> rustserver terminal tools >>>' "$bootstrap_home/.bashrc" 2>/dev/null; then
  bootstrap_skip 'All terminal tools and help/info commands are already installed; no changes required.'
  exit 0
fi

architecture="$(dpkg --print-architecture)"
case "$architecture" in
  amd64) release_arch=x86_64 ;;
  arm64) release_arch=aarch64 ;;
  *) bootstrap_warn "Optional TUI releases are unavailable for $architecture"; release_arch='' ;;
esac

work_dir="$(mktemp -d -t rustserver-terminal-tools.XXXXXXXX)"
trap 'rm -rf -- "$work_dir"' EXIT

install_reference() {
  require_regular_file "$TOOLS_SOURCE"
  if [[ -x /usr/local/bin/tools ]]; then
    bootstrap_skip 'Preserving existing tools reference.'
  elif [[ -e /usr/local/bin/tools || -L /usr/local/bin/tools ]]; then
    bootstrap_die 'Existing /usr/local/bin/tools is not executable; refusing to overwrite it.'
  else
    run_as_root install -D -m 0755 "$TOOLS_SOURCE" /usr/local/bin/tools
    bootstrap_info 'Installed tools command in /usr/local/bin.'
  fi

  # Only add interactive Bash functions. Calls with arguments keep normal Bash/GNU help behavior.
  local rc="$bootstrap_home/.bashrc"
  if ! grep -Fqx '# >>> rustserver terminal tools >>>' "$rc" 2>/dev/null; then
    cat <<'BASHRC' | run_as_root tee -a "$rc" >/dev/null

# >>> rustserver terminal tools >>>
export PATH="$HOME/.local/bin:$PATH"
help() { if (( $# == 0 )); then command tools; else builtin help "$@"; fi; }
info() { if (( $# == 0 )); then command tools; else command info "$@"; fi; }
# <<< rustserver terminal tools <<<
BASHRC
    run_as_root chown "$bootstrap_user" "$rc"
    bootstrap_info "Added help/info commands to $rc"
  else
    bootstrap_skip 'help/info are already configured.'
  fi
}

# GitHub Release API provides a SHA256 for each release asset. Never execute remote installer scripts.
get_release_asset() {
  local repo="$1" pattern="$2" metadata filename url digest
  local release_info="$work_dir/release.json"
  curl --fail --silent --show-error --location --retry 3 \
    --proto '=https' --tlsv1.2 "https://api.github.com/repos/$repo/releases/latest" \
    -o "$release_info" || return 1
  metadata="$(jq -er --arg pattern "$pattern" '
    [.assets[] | select(.name | test($pattern)) | [.name, .browser_download_url, .digest]] | .[0] | @tsv
  ' "$release_info")" || return 1
  IFS=$'\t' read -r filename url digest <<< "$metadata"
  [[ -n "$filename" && "$url" == https://github.com/* && "$digest" == sha256:* ]] || return 1
  downloaded="$work_dir/$filename"
  curl --fail --silent --show-error --location --retry 3 --proto '=https' --tlsv1.2 \
    "$url" -o "$downloaded" || return 1
  printf '%s  %s\n' "${digest#sha256:}" "$downloaded" | sha256sum --check --status -
}

install_release_deb() {
  local name="$1" repo="$2" pattern="$3"
  if command -v "$name" >/dev/null 2>&1; then
    bootstrap_skip "$name already installed."
    return
  fi
  bootstrap_info "Installing $name from $repo release."
  get_release_asset "$repo" "$pattern" || return 1
  run_as_root env DEBIAN_FRONTEND=noninteractive apt-get install -y "$downloaded"
  require_command "$name"
}

# Install missing upstream GitHub-release binaries. Never execute remote installers.
install_release_tarball_binary() {
  local name="$1" repository="$2" pattern="$3"
  local archive_dir archive_item
  if command -v "$name" >/dev/null 2>&1; then
    bootstrap_skip "$name already installed."
    return 0
  fi
  bootstrap_info "Installing $name from $repository release."
  get_release_asset "$repository" "$pattern" || return 1
  archive_dir="$work_dir/$name"
  mkdir -p "$archive_dir"
  if tar -tzf "$downloaded" | grep -Eq '(^/|(^|/)\.\.(/|$))'; then return 1; fi
  tar -xzf "$downloaded" -C "$archive_dir" || return 1
  archive_item="$(find "$archive_dir" -type f -name "$name" -print -quit)"
  [[ -n "$archive_item" ]] || return 1
  run_as_root install -D -m 0755 "$archive_item" "/usr/local/bin/$name"
  require_command "$name"
}

ensure_bat_command() {
  if command -v bat >/dev/null 2>&1; then return 0; fi
  command -v batcat >/dev/null 2>&1 || return 1
  run_as_root install -d -m 0755 /usr/local/bin
  run_as_root ln -s /usr/bin/batcat /usr/local/bin/bat
}

install_helix_tar() {
  if command -v hx >/dev/null 2>&1; then return; fi
  bootstrap_info 'Installing Helix editor and its runtime.'
  get_release_asset helix-editor/helix "^helix-[^/]+-${release_arch}-linux\\.tar\\.xz$" || return 1
  local extract_dir="$work_dir/helix"
  mkdir -p "$extract_dir"
  # Refuse archives with path traversal or absolute entries.
  if tar -tJf "$downloaded" | grep -Eq '(^/|(^|/)\.\.(/|$))'; then return 1; fi
  tar -xJf "$downloaded" -C "$extract_dir"
  local hx_file runtime_dir
  hx_file="$(find "$extract_dir" -type f -name hx -print -quit)"
  runtime_dir="$(find "$extract_dir" -type d -name runtime -print -quit)"
  [[ -n "$hx_file" && -n "$runtime_dir" ]] || return 1
  run_as_root install -D -m 0755 "$hx_file" /usr/local/bin/hx
  if ! run_as_root test -d /usr/local/lib/helix/runtime; then
    run_as_root mkdir -p /usr/local/lib/helix
    run_as_root cp -a "$runtime_dir" /usr/local/lib/helix/runtime
  fi
}

install_posting() {
  if [[ -x "$bootstrap_home/.local/bin/posting" ]] || command -v posting >/dev/null 2>&1; then
    bootstrap_skip 'posting already installed.'
    return
  fi
  if [[ "$bootstrap_user" == root ]]; then
    bootstrap_warn 'Posting needs a non-root SSH user; skipping.'
    return 1
  fi
  bootstrap_info "Installing Posting for $bootstrap_user through pipx."
  sudo -u "$bootstrap_user" env HOME="$bootstrap_home" \
    PIPX_BIN_DIR="$bootstrap_home/.local/bin" \
    PIPX_HOME="$bootstrap_home/.local/share/pipx" pipx install posting
  [[ -x "$bootstrap_home/.local/bin/posting" ]]
}

optional() {
  local display="$1"; shift
  if ! "$@"; then
    bootstrap_warn "Optional tool $display could not be installed; continuing. Missing tools can be installed during the next Bootstrap run."
  fi
}

require_command curl
ensure_sudo_access
# Install only commands missing from the machine. No APT call on subsequent runs.
declare -a missing_apt_packages=()
for package_name in jq mc bat ripgrep micro btop tmux ncdu fzf duf lnav; do
  binary_name="$package_name"
  case "$package_name" in
    ripgrep) binary_name=rg ;;
    bat) binary_name=batcat ;;
  esac
  if ! command -v "$binary_name" >/dev/null 2>&1; then
    missing_apt_packages+=("$package_name")
  fi
done
if ! [[ -x "$bootstrap_home/.local/bin/posting" ]] &&
   ! command -v posting >/dev/null 2>&1 &&
   ! command -v pipx >/dev/null 2>&1; then
  missing_apt_packages+=(pipx)
fi
if ! command -v hx >/dev/null 2>&1 && ! command -v xz >/dev/null 2>&1; then
  missing_apt_packages+=(xz-utils)
fi
if (( ${#missing_apt_packages[@]} > 0 )); then
  bootstrap_info "Installing missing Ubuntu terminal packages: ${missing_apt_packages[*]}"
  run_as_root apt-get update
  run_as_root env DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends "${missing_apt_packages[@]}"
else
  bootstrap_skip 'APT terminal packages are already installed.'
fi

require_command jq
ensure_bat_command
install_reference

if [[ -n "$release_arch" ]]; then
  if [[ "$architecture" == amd64 ]]; then
    optional lazygit install_release_tarball_binary lazygit jesseduffield/lazygit       '^lazygit_[^/]+_linux_x86_64\.tar\.gz$'
    optional lazydocker install_release_tarball_binary lazydocker jesseduffield/lazydocker       '^lazydocker_[^/]+_Linux_x86_64\.tar\.gz$'
    optional termide install_release_tarball_binary termide termide/termide       '^termide-[^/]+-x86_64-unknown-linux-gnu\.tar\.gz$'
  else
    optional lazygit install_release_tarball_binary lazygit jesseduffield/lazygit       '^lazygit_[^/]+_linux_arm64\.tar\.gz$'
    optional lazydocker install_release_tarball_binary lazydocker jesseduffield/lazydocker       '^lazydocker_[^/]+_Linux_arm64\.tar\.gz$'
    optional termide install_release_tarball_binary termide termide/termide       '^termide-[^/]+-aarch64-unknown-linux-gnu\.tar\.gz$'
  fi
  optional systemctl-tui install_release_deb systemctl-tui rgwood/systemctl-tui \
    "^systemctl-tui_[^/]+_${architecture}\\.deb$"
  optional lazyjournal install_release_deb lazyjournal Lifailon/lazyjournal \
    "^lazyjournal-[^/]+-${architecture}\\.deb$"
  optional yazi install_release_deb yazi sxyazi/yazi \
    "^yazi-${release_arch}-unknown-linux-gnu\\.deb$"
  if [[ "$architecture" == amd64 ]]; then
    optional dust install_release_deb dust bootandy/dust \
      "^du-dust_[^/]+_${architecture}\\.deb$"
  fi
  optional helix install_helix_tar
fi
optional posting install_posting
bootstrap_info 'Terminal tools step complete. Reconnect SSH, then run: tools, help, or info.'
