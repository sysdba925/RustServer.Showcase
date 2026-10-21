#!/usr/bin/env bash
# Shared helpers for the production updater bootstrap. Keep step-specific work
# in the numbered scripts.

BOOTSTRAP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
readonly BOOTSTRAP_DIR
readonly BOOTSTRAP_DEFAULTS_FILE="$BOOTSTRAP_DIR/bootstrap.env.example"
readonly UPDATER_ASSEMBLY="RustServer.Updater.Api.dll"
readonly DEFAULT_BOOTSTRAP_MODE="install"
readonly DEFAULT_BOOTSTRAP_LOCK_FILE="/run/lock/rustserver/bootstrap.lock"

bootstrap_log() {
  local level="$1"
  shift
  printf '[RUSTSERVER BOOTSTRAP] %-4s %s\n' "$level" "$*"
}

bootstrap_info() {
  bootstrap_log "INFO" "$*"
}

bootstrap_skip() {
  bootstrap_log "SKIP" "$*"
}

bootstrap_die() {
  local message="$1"
  local exit_code="${2:-1}"

  bootstrap_log "FAIL" "$message" >&2
  exit "$exit_code"
}

require_command() {
  local command_name="$1"

  command -v "$command_name" >/dev/null 2>&1 || \
    bootstrap_die "Required command is unavailable: $command_name"
}

ensure_sudo_access() {
  if (( EUID == 0 )); then
    return
  fi

  require_command sudo
  sudo -n true >/dev/null 2>&1 || \
    bootstrap_die "Passwordless sudo access is required. Configure it before running Bootstrap."
}

run_as_root() {
  if (( EUID == 0 )); then
    command "$@"
  else
    sudo -n "$@"
  fi
}

bootstrap_mode() {
  printf '%s\n' "${RUSTSERVER_BOOTSTRAP_MODE:-$DEFAULT_BOOTSTRAP_MODE}"
}

bootstrap_mode_is() {
  [[ "$(bootstrap_mode)" == "$1" ]]
}

release_tag_is_valid() {
  local release_tag="$1"

  [[ "$release_tag" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]
}

validate_bootstrap_invocation() {
  local mode

  mode="$(bootstrap_mode)"
  case "$mode" in
    install)
      [[ -z "${RUSTSERVER_UPDATER_RELEASE_TAG_OVERRIDE:-}" ]] || \
        bootstrap_die "RUSTSERVER_UPDATER_RELEASE_TAG_OVERRIDE is not supported"
      ;;
    *)
      bootstrap_die "Unsupported Bootstrap mode: $mode"
      ;;
  esac
}

acquire_bootstrap_lock() {
  local lock_file="${RUSTSERVER_BOOTSTRAP_LOCK_FILE:-$DEFAULT_BOOTSTRAP_LOCK_FILE}"
  local lock_directory

  require_command flock

  if [[ -z "${RUSTSERVER_BOOTSTRAP_LOCK_FILE:-}" ]]; then
    lock_directory="$(dirname -- "$lock_file")"
    ensure_sudo_access
    run_as_root install -d -m 0755 -o root -g root -- "$lock_directory" || \
      bootstrap_die "Could not create Bootstrap lock directory: $lock_directory"

    if root_path_exists "$lock_file"; then
      require_root_regular_file "$lock_file"
    else
      run_as_root install -m 0666 -o root -g root /dev/null "$lock_file" || \
        bootstrap_die "Could not create Bootstrap lock file: $lock_file"
    fi
  else
    [[ -f "$lock_file" && ! -L "$lock_file" ]] || \
      bootstrap_die "Bootstrap lock file is missing or unsafe: $lock_file"
  fi

  # The root-owned file is intentionally world-writable but lives in a
  # root-owned directory. Users can take an advisory lock, not replace it.
  exec {BOOTSTRAP_LOCK_FD}>"$lock_file"
  flock -n "$BOOTSTRAP_LOCK_FD" || \
    bootstrap_die "Another RustServer Bootstrap is already running"
}

require_regular_file() {
  local path="$1"

  [[ -f "$path" && ! -L "$path" ]] || \
    bootstrap_die "Required regular file is missing: $path"
}

require_private_token_file() {
  local token_file="$1"
  local mode

  [[ -f "$token_file" && ! -L "$token_file" ]] || \
    bootstrap_die "GitHub token file is missing or unsafe: $token_file"
  mode="$(stat -c '%a' -- "$token_file")"
  (( (8#$mode & 077) == 0 )) || \
    bootstrap_die "GitHub token file must not be readable by group or other users"
}

require_root_regular_file() {
  local path="$1"

  if [[ -f "$path" && ! -L "$path" ]]; then
    return
  fi

  if ! run_as_root test -f "$path" || run_as_root test -L "$path"; then
    bootstrap_die "Required regular file is missing or unsafe: $path"
  fi
}

read_root_regular_file() {
  local path="$1"

  require_root_regular_file "$path"
  if [[ -r "$path" ]]; then
    cat -- "$path"
  else
    run_as_root cat -- "$path"
  fi
}

root_path_exists() {
  local path="$1"

  [[ -e "$path" || -L "$path" ]] || \
    run_as_root test -e "$path" || run_as_root test -L "$path"
}

require_directory() {
  local path="$1"

  [[ -d "$path" && ! -L "$path" ]] || \
    bootstrap_die "Required directory is missing or unsafe: $path"
}

ensure_root_directory() {
  local path="$1"
  local mode="$2"
  local owner="$3"
  local group="$4"

  if [[ -e "$path" || -L "$path" ]]; then
    require_directory "$path"
    bootstrap_skip "Preserving existing directory: $path"
    return
  fi

  run_as_root install -d -m "$mode" -o "$owner" -g "$group" -- "$path" || \
    bootstrap_die "Could not create directory: $path"
  bootstrap_info "Created directory: $path"
}

set_bootstrap_option() {
  local key="$1"
  local value="$2"

  case "$key" in
    RUSTSERVER_UPDATER_BIND_ADDRESS|RUSTSERVER_UPDATER_PORT|GITHUB_TOKEN_FILE)
      printf -v "$key" '%s' "$value"
      ;;
    *)
      bootstrap_die "Unsupported key in Bootstrap defaults: $key"
      ;;
  esac
}

# Deliberately parse instead of source: installation defaults must be data,
# not shell code. Values are intentionally plain KEY=value assignments.
load_bootstrap_defaults() {
  local defaults_file="$BOOTSTRAP_DEFAULTS_FILE"
  local line
  local key
  local value
  local line_number=0
  local -A seen_keys=()
  local -A environment_overrides=()
  local -a option_keys=(
    RUSTSERVER_UPDATER_BIND_ADDRESS
    RUSTSERVER_UPDATER_PORT
    GITHUB_TOKEN_FILE
  )

  require_regular_file "$defaults_file"
  for key in "${option_keys[@]}"; do
    if [[ -v "$key" ]]; then
      environment_overrides["$key"]="${!key}"
    fi
    unset "$key"
  done

  while IFS= read -r line || [[ -n "$line" ]]; do
    line_number=$((line_number + 1))
    line="${line%$'\r'}"

    [[ -z "$line" || "$line" == \#* ]] && continue
    if [[ ! "$line" =~ ^([A-Z][A-Z0-9_]*)=(.*)$ ]]; then
      bootstrap_die "Invalid Bootstrap defaults syntax at $defaults_file:$line_number; use KEY=value"
    fi

    key="${BASH_REMATCH[1]}"
    value="${BASH_REMATCH[2]}"
    if [[ -n "${seen_keys[$key]:-}" ]]; then
      bootstrap_die "Bootstrap defaults key is defined more than once: $key"
    fi
    seen_keys["$key"]=1
    set_bootstrap_option "$key" "$value"
  done < "$defaults_file"

  for key in "${option_keys[@]}"; do
    if [[ -n "${environment_overrides[$key]+set}" ]]; then
      set_bootstrap_option "$key" "${environment_overrides[$key]}"
    fi
  done

  for key in RUSTSERVER_UPDATER_BIND_ADDRESS RUSTSERVER_UPDATER_PORT; do
    [[ -n "${!key:-}" ]] || \
      bootstrap_die "Required Bootstrap default is missing or empty: $key"
  done

  GITHUB_TOKEN_FILE="${GITHUB_TOKEN_FILE:-}"
}

validate_release_tag() {
  release_tag_is_valid "$RUSTSERVER_UPDATER_RELEASE_TAG" || \
    bootstrap_die "RUSTSERVER_UPDATER_RELEASE_TAG contains unsupported characters"
}

set_updater_release_assets() {
  local release_tag="$1"

  release_tag_is_valid "$release_tag" || \
    bootstrap_die "Updater release tag contains unsupported characters"
  RUSTSERVER_UPDATER_RELEASE_TAG="$release_tag"
  RUSTSERVER_UPDATER_ASSET="RustServer.Updater-${release_tag}-linux-x64.tar.gz"
  RUSTSERVER_UPDATER_CHECKSUM_ASSET="${RUSTSERVER_UPDATER_ASSET}.sha256"
}

validate_asset_name() {
  local name="$1"
  local setting_name="$2"

  [[ "$name" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ && "$name" != .* && "$name" != *..* ]] || \
    bootstrap_die "$setting_name must be a safe file name"
}

validate_ipv4_address() {
  local address="$1"
  local octet
  local -a octets=()

  [[ "$address" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ ]] || \
    bootstrap_die "RUSTSERVER_UPDATER_BIND_ADDRESS must be an IPv4 address"
  IFS='.' read -r -a octets <<< "$address"
  for octet in "${octets[@]}"; do
    (( 10#$octet <= 255 )) || \
      bootstrap_die "RUSTSERVER_UPDATER_BIND_ADDRESS must be a valid IPv4 address"
  done
}

validate_port() {
  local port="$1"

  if [[ ! "$port" =~ ^[0-9]{1,5}$ ]] || (( 10#$port < 1 || 10#$port > 65535 )); then
    bootstrap_die "RUSTSERVER_UPDATER_PORT must be an integer from 1 through 65535"
  fi
}

validate_updater_release_configuration() {
  validate_release_tag
  validate_asset_name "$RUSTSERVER_UPDATER_ASSET" "RUSTSERVER_UPDATER_ASSET"
  validate_asset_name "$RUSTSERVER_UPDATER_CHECKSUM_ASSET" "RUSTSERVER_UPDATER_CHECKSUM_ASSET"
}

validate_updater_configuration() {
  validate_ipv4_address "$RUSTSERVER_UPDATER_BIND_ADDRESS"
  validate_port "$RUSTSERVER_UPDATER_PORT"
}

environment_value() {
  local environment_file="$1"
  local wanted_key="$2"
  local line
  local key
  local current_value
  local selected_value=""
  local count=0

  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ -z "$line" || "$line" == \#* ]] && continue
    [[ "$line" == *=* ]] || \
      bootstrap_die "Invalid environment syntax in $environment_file"

    key="${line%%=*}"
    current_value="${line#*=}"
    if [[ "$key" == "$wanted_key" ]]; then
      count=$((count + 1))
      selected_value="$current_value"
    fi
  done < <(read_root_regular_file "$environment_file")

  (( count == 1 )) || \
    bootstrap_die "Environment key '$wanted_key' must be defined exactly once in $environment_file"
  printf '%s\n' "$selected_value"
}

dotnet_has_aspnetcore_runtime_10() {
  command -v dotnet >/dev/null 2>&1 && \
    dotnet --list-runtimes | awk '$1 == "Microsoft.AspNetCore.App" && $2 ~ /^10\./ { found = 1 } END { exit !found }'
}

updater_current_directory() {
  local current_link="/opt/rustserver/updater/current"
  local releases_directory="/opt/rustserver/updater/releases"
  local target

  if [[ ! -L "$current_link" ]]; then
    bootstrap_die "Updater current path is not a symbolic link: $current_link"
  fi

  target="$(readlink -e -- "$current_link")" || \
    bootstrap_die "Updater current symbolic link is dangling: $current_link"
  [[ "$target" == "$releases_directory/"* && -d "$target" ]] || \
    bootstrap_die "Updater current symbolic link points outside releases: $current_link"
  [[ -f "$target/api/$UPDATER_ASSEMBLY" && ! -L "$target/api/$UPDATER_ASSEMBLY" ]] || \
    bootstrap_die "Updater bundled API assembly is missing from the active release: $target"
  printf '%s\n' "$target"
}

updater_current_release_tag() {
  local releases_directory="/opt/rustserver/updater/releases"
  local target
  local release_tag

  target="$(updater_current_directory)"
  [[ "$(dirname -- "$target")" == "$releases_directory" ]] || \
    bootstrap_die "Updater current symbolic link must point directly to a release directory"
  release_tag="${target##*/}"
  release_tag_is_valid "$release_tag" || \
    bootstrap_die "Updater current symbolic link has an unsafe release tag"
  printf '%s\n' "$release_tag"
}

updater_health_url() {
  local environment_file="${1:-/etc/rustserver/updater.env}"
  local bind_address
  local port
  local host

  bind_address="$(environment_value "$environment_file" "RUSTSERVER_UPDATER_BIND_ADDRESS")"
  port="$(environment_value "$environment_file" "RUSTSERVER_UPDATER_PORT")"
  validate_ipv4_address "$bind_address"
  validate_port "$port"

  host="$bind_address"
  if [[ "$host" == "0.0.0.0" ]]; then
    host="127.0.0.1"
  fi
  printf 'http://%s:%s/health\n' "$host" "$port"
}

wait_for_updater_health() {
  local url="$1"
  local remaining_attempts="${RUSTSERVER_UPDATER_HEALTH_ATTEMPTS:-15}"
  local status_code=""

  [[ "$remaining_attempts" =~ ^[1-9][0-9]*$ ]] || \
    bootstrap_die "RUSTSERVER_UPDATER_HEALTH_ATTEMPTS must be a positive integer"

  while (( remaining_attempts > 0 )); do
    status_code="$(curl --noproxy '*' --silent --show-error --output /dev/null --write-out '%{http_code}' \
      --connect-timeout 2 --max-time 5 "$url" || true)"
    if [[ "$status_code" == "200" ]]; then
      bootstrap_info "Updater health endpoint returned HTTP 200: $url"
      return 0
    fi
    remaining_attempts=$((remaining_attempts - 1))
    sleep 1
  done

  bootstrap_log "FAIL" "Updater health endpoint did not return HTTP 200: $url (last status: ${status_code:-connection failed})" >&2
  return 1
}

verify_updater_health() {
  local url="$1"

  wait_for_updater_health "$url" || \
    bootstrap_die "Updater health endpoint did not return HTTP 200: $url"
}
