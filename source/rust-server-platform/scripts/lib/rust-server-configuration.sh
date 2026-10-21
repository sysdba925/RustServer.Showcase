#!/usr/bin/env bash
# Rust Dedicated configuration parsing and non-destructive migration helpers.
#
# Callers must define run_as_root before calling rust_server_migrate_configuration.

rust_server_configuration_error() {
  local message="$1"

  if declare -F bootstrap_die >/dev/null 2>&1; then
    bootstrap_die "$message"
  fi

  printf 'Error: %s\n' "$message" >&2
  return 1
}

rust_server_configuration_info() {
  local message="$1"

  if declare -F bootstrap_info >/dev/null 2>&1; then
    bootstrap_info "$message"
    return
  fi

  printf 'Info: %s\n' "$message"
}

rust_server_configuration_skip() {
  local message="$1"

  if declare -F bootstrap_skip >/dev/null 2>&1; then
    bootstrap_skip "$message"
    return
  fi

  printf 'Skip: %s\n' "$message"
}

rust_server_env_read_value() {
  local environment_file="$1"
  local key="$2"

  [[ -f "$environment_file" && ! -L "$environment_file" ]] || return 1
  [[ "$key" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || return 2

  # Do not source runtime configuration. It is administrator-controlled input,
  # and Compose-style values must never be evaluated as shell code.
  awk -v key="$key" '
    function trim(value) {
      sub(/^[[:space:]]+/, "", value)
      sub(/[[:space:]]+$/, "", value)
      return value
    }
    {
      line = $0
      sub(/\r$/, "", line)
      if (line ~ /^[[:space:]]*#/ || line ~ /^[[:space:]]*$/) next

      pattern = "^[[:space:]]*" key "[[:space:]]*="
      if (line ~ pattern) {
        value = trim(line)
        sub(pattern, "", value)
        value = trim(value)
        if ((value ~ /^".*"$/) || (value ~ /^\047.*\047$/)) {
          value = substr(value, 2, length(value) - 2)
        } else {
          sub(/[[:space:]]+#.*$/, "", value)
          value = trim(value)
        }
        found = 1
      }
    }
    END {
      if (found) print value
      else exit 1
    }
  ' "$environment_file"
}

rust_server_export_environment() {
  local environment_file="$1"
  local key
  local value
  local -a keys=(
    RUST_SERVER_PATH
    RUST_SERVER_PORT
    RUST_SERVER_QUERY_PORT
    RUST_SERVER_LEVEL
    RUST_SERVER_SEED
    RUST_SERVER_WORLD_SIZE
    RUST_SERVER_MAX_PLAYERS
    RUST_SERVER_HOSTNAME
    RUST_SERVER_DESCRIPTION
    RUST_SERVER_TICK_RATE
    RUST_SERVER_SAVE_INTERVAL
    RUST_SERVER_IDENTITY
  )

  for key in "${keys[@]}"; do
    if value="$(rust_server_env_read_value "$environment_file" "$key")"; then
      export "$key=$value"
    fi
  done
}

rust_server_validate_configuration() {
  local environment_file="$1"
  local rust_server_path

  [[ -f "$environment_file" && ! -L "$environment_file" ]] || {
    rust_server_configuration_error "Rust server configuration is missing or is not a regular file: $environment_file"
    return 1
  }

  rust_server_path="$(rust_server_env_read_value "$environment_file" 'RUST_SERVER_PATH')" || {
    rust_server_configuration_error "RUST_SERVER_PATH is missing from Rust server configuration: $environment_file"
    return 1
  }
  [[ -n "$rust_server_path" ]] || {
    rust_server_configuration_error "RUST_SERVER_PATH is empty in Rust server configuration: $environment_file"
    return 1
  }
  [[ "$rust_server_path" == /* ]] || {
    rust_server_configuration_error "RUST_SERVER_PATH must be an absolute path in Rust server configuration: $environment_file"
    return 1
  }
}

rust_server_copy_configuration_without_overwrite() {
  local source_file="$1"
  local target_file="$2"
  local target_directory
  local temporary_file

  target_directory="$(dirname -- "$target_file")"
  run_as_root install -d -m 0755 -- "$target_directory" || {
    rust_server_configuration_error "Failed to create Rust server configuration directory: $target_directory"
    return 1
  }

  temporary_file="$(run_as_root mktemp "$target_directory/.rust-server.env.tmp.XXXXXX")" || {
    rust_server_configuration_error "Failed to create a temporary Rust server configuration in: $target_directory"
    return 1
  }

  if ! run_as_root install -m 0644 -- "$source_file" "$temporary_file"; then
    run_as_root rm -f -- "$temporary_file" || true
    rust_server_configuration_error "Failed to copy Rust server configuration to a temporary file"
    return 1
  fi

  # A hard link gives the destination an atomic, no-replace creation. Do not
  # overwrite a configuration created concurrently by another Bootstrap run.
  if ! run_as_root ln -- "$temporary_file" "$target_file"; then
    run_as_root rm -f -- "$temporary_file" || true
    rust_server_configuration_error "Rust server configuration appeared while it was being created; the existing file was preserved: $target_file"
    return 1
  fi

  run_as_root rm -f -- "$temporary_file" || {
    rust_server_configuration_error "Rust server configuration was created but the temporary file could not be removed: $temporary_file"
    return 1
  }
}

rust_server_migrate_configuration() {
  local legacy_file="$1"
  local target_file="$2"
  local template_file="$3"
  local legacy_exists=0

  if [[ -e "$target_file" || -L "$target_file" ]]; then
    [[ -f "$target_file" && ! -L "$target_file" ]] || {
      rust_server_configuration_error "Rust server configuration target is not a regular file: $target_file"
      return 1
    }
    rust_server_validate_configuration "$target_file" || return 1
    rust_server_configuration_skip "Preserving authoritative canonical Rust server configuration: $target_file"
    return
  fi

  if [[ -e "$legacy_file" || -L "$legacy_file" ]]; then
    [[ -f "$legacy_file" && ! -L "$legacy_file" ]] || {
      rust_server_configuration_error "Legacy Rust server configuration is not a regular file: $legacy_file"
      return 1
    }
    legacy_exists=1
  fi

  if (( legacy_exists )); then
    rust_server_validate_configuration "$legacy_file" || return 1
    rust_server_copy_configuration_without_overwrite "$legacy_file" "$target_file" || return 1
    rust_server_configuration_info "Migrated Rust server configuration to $target_file; preserved legacy file as a recoverable backup: $legacy_file"
  else
    [[ -f "$template_file" && ! -L "$template_file" ]] || {
      rust_server_configuration_error "Rust server configuration template is missing or is not a regular file: $template_file"
      return 1
    }
    rust_server_validate_configuration "$template_file" || return 1
    rust_server_copy_configuration_without_overwrite "$template_file" "$target_file" || return 1
    rust_server_configuration_info "Created canonical Rust server configuration from template: $target_file"
  fi

  rust_server_validate_configuration "$target_file"
}
