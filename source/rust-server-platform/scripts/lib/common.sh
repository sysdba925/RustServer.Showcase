#!/usr/bin/env bash
# Generic bootstrap helpers. Step-specific paths and validation stay local.
ensure_sudo_access() {
  if (( EUID == 0 )); then
    return
  fi

  require_command sudo

  sudo -n true || \
    bootstrap_die "Passwordless sudo access is required."
}

bootstrap_log() {
  local level="$1"
  shift
  printf '[BOOTSTRAP] %-4s %s\n' "$level" "$*"
}

bootstrap_info() {
  bootstrap_log "INFO" "$*"
}

bootstrap_skip() {
  bootstrap_log "SKIP" "$*"
}

bootstrap_warn() {
  bootstrap_log "WARN" "$*" >&2
}

bootstrap_error() {
  bootstrap_log "ERROR" "$*" >&2
}

bootstrap_die() {
  local message="$1"
  local exit_code="${2:-1}"
  bootstrap_error "$message"
  exit "$exit_code"
}

require_command() {
  local command_name="$1"

  command -v "$command_name" >/dev/null 2>&1 || \
    bootstrap_die "Required command is not available: $command_name"
}

require_regular_file() {
  local path="$1"

  [[ -f "$path" ]] || bootstrap_die "Required file does not exist: $path"
}

require_nonempty_file() {
  local path="$1"

  require_regular_file "$path"
  [[ -s "$path" ]] || bootstrap_die "Required file is empty: $path"
}
