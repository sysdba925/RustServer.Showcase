#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly RUST_SERVER_CONFIG_DIR="$PLATFORM_DIR/docker/rust-server"
readonly TOPOLOGY_TEMPLATE="$PLATFORM_DIR/config/topology.env.example"
readonly TOPOLOGY_DIRECTORY="/etc/rustserver"
readonly LEGACY_RUST_SERVER_ENV_FILE="$RUST_SERVER_CONFIG_DIR/.env"
readonly RUST_SERVER_ENV_FILE="$TOPOLOGY_DIRECTORY/rust-server.env"
readonly RUST_SERVER_ENV_TEMPLATE="$RUST_SERVER_CONFIG_DIR/.env.example"
readonly LEGACY_RCON_PASSWORD_FILE="$RUST_SERVER_CONFIG_DIR/.rcon-password"
readonly RCON_PASSWORD_FILE="$TOPOLOGY_DIRECTORY/rcon-password"
readonly RCON_PASSWORD_OWNER="$(stat -c '%U' -- "$PLATFORM_DIR/..")"
readonly RCON_PASSWORD_GROUP="$(stat -c '%G' -- "$PLATFORM_DIR/..")"
readonly -a TOPOLOGY_RCON_GRPC_URL_DEFAULTS=(
  'RUSTSERVER_RCON_GRPC_URL=http://127.0.0.1:50051'
  'RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL=http://127.0.0.1:50052'
)
readonly -a TOPOLOGY_BIND_HOST_DEFAULTS=(
  'RUSTSERVER_CONTROL_BIND_HOST=0.0.0.0'
  'RUSTSERVER_VERSIONS_BIND_HOST=0.0.0.0'
  'RUSTSERVER_PLUGINS_BIND_HOST=0.0.0.0'
  'RUSTSERVER_SNAPSHOTS_BIND_HOST=0.0.0.0'
  'RUSTSERVER_WEB_BIND_HOST=0.0.0.0'
)
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"
# shellcheck source=../lib/rust-server-configuration.sh
source "$STEP_DIR/../lib/rust-server-configuration.sh"
# shellcheck source=../lib/topology.sh
source "$STEP_DIR/../lib/topology.sh"

readonly RUST_SERVER_COMPOSE_FILE="$RUST_SERVER_CONFIG_DIR/compose.yml"

# RUST_SERVER_PATH is the only non-secret value Compose requires explicitly.
# The remaining Rust server settings have defaults in compose.yml.
readonly -a REQUIRED_RUST_SERVER_ENV_KEYS=(
  "RUST_SERVER_PATH"
)

run_as_root() {
  if (( EUID == 0 )); then
    command "$@"
  else
    sudo "$@"
  fi
}

create_topology_file_if_missing() {
  if [[ -e "$RUSTSERVER_TOPOLOGY_FILE" || -L "$RUSTSERVER_TOPOLOGY_FILE" ]]; then
    [[ -f "$RUSTSERVER_TOPOLOGY_FILE" && ! -L "$RUSTSERVER_TOPOLOGY_FILE" ]] || \
      bootstrap_die "Topology path is not a regular file: $RUSTSERVER_TOPOLOGY_FILE"
    bootstrap_skip "Preserving existing topology: $RUSTSERVER_TOPOLOGY_FILE"
    return
  fi

  run_as_root install -d -m 0755 -- "$TOPOLOGY_DIRECTORY" || \
    bootstrap_die "Failed to create topology directory: $TOPOLOGY_DIRECTORY"
  run_as_root install -m 0644 -- "$TOPOLOGY_TEMPLATE" "$RUSTSERVER_TOPOLOGY_FILE" || \
    bootstrap_die "Failed to initialize topology from template: $RUSTSERVER_TOPOLOGY_FILE"
  bootstrap_info "Created topology from template: $RUSTSERVER_TOPOLOGY_FILE"
}

topology_key_definition_count() {
  local key="$1"

  awk -v key="$key" '
    {
      line = $0
      sub(/\r$/, "", line)
      if (line ~ /^[[:space:]]*#/ || line ~ /^[[:space:]]*$/) {
        next
      }

      pattern = "^[[:space:]]*" key "[[:space:]]*="
      if (line ~ pattern) {
        count += 1
      }
    }
    END {
      print count + 0
    }
  ' "$RUSTSERVER_TOPOLOGY_FILE"
}

ensure_topology_rcon_grpc_urls() {
  local entry
  local key
  local definition_count
  local -a missing_entries=()

  for entry in "${TOPOLOGY_RCON_GRPC_URL_DEFAULTS[@]}"; do
    key="${entry%%=*}"
    definition_count="$(topology_key_definition_count "$key")"
    if (( definition_count > 1 )); then
      bootstrap_die "Topology key '$key' is defined more than once in $RUSTSERVER_TOPOLOGY_FILE"
    fi

    if (( definition_count == 0 )); then
      missing_entries+=("$entry")
    fi
  done

  if (( ${#missing_entries[@]} == 0 )); then
    bootstrap_skip "RCON gRPC topology URLs are already configured"
    return
  fi

  {
    printf '\n# LOCAL RCON GRPC CONFIGURATION\n'
    printf '%s\n' '# The RCON gRPC and Swagger/OpenAPI endpoints remain loopback-only.'
    printf '%s\n' "${missing_entries[@]}"
  } | run_as_root tee -a -- "$RUSTSERVER_TOPOLOGY_FILE" >/dev/null || \
    bootstrap_die "Failed to add missing RCON gRPC topology URLs to $RUSTSERVER_TOPOLOGY_FILE"

  bootstrap_info "Added missing RCON gRPC topology URLs using development defaults"
}

ensure_topology_bind_hosts() {
  local entry
  local key
  local definition_count
  local -a missing_entries=()

  for entry in "${TOPOLOGY_BIND_HOST_DEFAULTS[@]}"; do
    key="${entry%%=*}"
    definition_count="$(topology_key_definition_count "$key")"
    if (( definition_count > 1 )); then
      bootstrap_die "Topology key '$key' is defined more than once in $RUSTSERVER_TOPOLOGY_FILE"
    fi

    if (( definition_count == 0 )); then
      missing_entries+=("$entry")
    fi
  done

  if (( ${#missing_entries[@]} == 0 )); then
    bootstrap_skip "Topology bind hosts are already configured"
    return
  fi

  {
    printf '\n# TEMPORARY DEVELOPMENT CONFIGURATION\n'
    printf '%s\n' '# All HTTP services are reachable from the trusted development network.'
    printf '%s\n' '# Before public deployment, change the five backend bind hosts to 127.0.0.1.'
    printf '%s\n' '# Web remains accessible externally.'
    printf '%s\n' "${missing_entries[@]}"
  } | run_as_root tee -a -- "$RUSTSERVER_TOPOLOGY_FILE" >/dev/null || \
    bootstrap_die "Failed to add missing topology bind hosts to $RUSTSERVER_TOPOLOGY_FILE"

  bootstrap_info "Added missing topology bind hosts using development defaults"
}

require_nonempty_env_key() {
  local key="$1"

  # Do not source .env: it is user-controlled input. This validates that the
  # final definition of a required Compose key has a non-empty raw value while
  # leaving Compose to interpret quoting and comments according to its grammar.
  if ! awk -v key="$key" '
    {
      line = $0
      sub(/\r$/, "", line)
      if (line ~ /^[[:space:]]*#/ || line ~ /^[[:space:]]*$/) {
        next
      }

      pattern = "^[[:space:]]*" key "[[:space:]]*="
      if (line ~ pattern) {
        value = line
        sub(pattern, "", value)
        sub(/^[[:space:]]+/, "", value)
        sub(/[[:space:]]+$/, "", value)
        found = 1
      }
    }
    END {
      exit !(found && length(value) > 0)
    }
  ' "$RUST_SERVER_ENV_FILE"; then
    bootstrap_die "Required Rust server configuration key is missing or empty: $key"
  fi
}

remove_temporary_rcon_password_file() {
  local temporary_password_file="$1"

  run_as_root rm -f -- "$temporary_password_file"
}

link_rcon_password_file_without_overwrite() {
  local temporary_password_file="$1"
  local success_message="$2"

  # A hard link creates the final name without overwriting a secret another
  # process may have created after the initial absence check.
  if ! run_as_root ln -- "$temporary_password_file" "$RCON_PASSWORD_FILE"; then
    remove_temporary_rcon_password_file "$temporary_password_file"
    [[ -f "$RCON_PASSWORD_FILE" && ! -L "$RCON_PASSWORD_FILE" ]] || \
      bootstrap_die "Failed to install the RCON password file: $RCON_PASSWORD_FILE"
    bootstrap_skip "RCON password file appeared during migration; preserving the canonical secret"
    return
  fi

  remove_temporary_rcon_password_file "$temporary_password_file"
  bootstrap_info "$success_message"
}

copy_legacy_rcon_password() {
  local temporary_password_file

  [[ -f "$LEGACY_RCON_PASSWORD_FILE" && ! -L "$LEGACY_RCON_PASSWORD_FILE" ]] || \
    bootstrap_die "Legacy RCON password path is not a regular file: $LEGACY_RCON_PASSWORD_FILE"
  run_as_root test -s "$LEGACY_RCON_PASSWORD_FILE" || \
    bootstrap_die "Legacy RCON password file is empty: $LEGACY_RCON_PASSWORD_FILE"

  temporary_password_file="$(run_as_root mktemp "$TOPOLOGY_DIRECTORY/.rcon-password.tmp.XXXXXX")" || \
    bootstrap_die "Failed to create a temporary RCON password file"

  if ! run_as_root install -m 0600 -o "$RCON_PASSWORD_OWNER" -g "$RCON_PASSWORD_GROUP" \
    -- "$LEGACY_RCON_PASSWORD_FILE" "$temporary_password_file"; then
    remove_temporary_rcon_password_file "$temporary_password_file"
    bootstrap_die "Failed to copy the legacy RCON password"
  fi

  link_rcon_password_file_without_overwrite \
    "$temporary_password_file" \
    "Migrated legacy RCON password to $RCON_PASSWORD_FILE"
}

generate_rcon_password() {
  local temporary_password_file

  require_command "openssl"
  temporary_password_file="$(run_as_root mktemp "$TOPOLOGY_DIRECTORY/.rcon-password.tmp.XXXXXX")" || \
    bootstrap_die "Failed to create a temporary RCON password file"

  if ! openssl rand -hex 32 | run_as_root tee -- "$temporary_password_file" >/dev/null; then
    remove_temporary_rcon_password_file "$temporary_password_file"
    bootstrap_die "Failed to generate the RCON password"
  fi

  if ! run_as_root test -s "$temporary_password_file"; then
    remove_temporary_rcon_password_file "$temporary_password_file"
    bootstrap_die "Generated RCON password is empty"
  fi

  if ! run_as_root chown "$RCON_PASSWORD_OWNER:$RCON_PASSWORD_GROUP" -- "$temporary_password_file" || \
    ! run_as_root chmod 600 -- "$temporary_password_file"; then
    remove_temporary_rcon_password_file "$temporary_password_file"
    bootstrap_die "Failed to restrict permissions on the generated RCON password"
  fi

  link_rcon_password_file_without_overwrite \
    "$temporary_password_file" \
    "Generated RCON password file: $RCON_PASSWORD_FILE"
}

ensure_rcon_password() {
  local mode
  local owner
  local group

  if [[ -e "$RCON_PASSWORD_FILE" || -L "$RCON_PASSWORD_FILE" ]]; then
    [[ -f "$RCON_PASSWORD_FILE" && ! -L "$RCON_PASSWORD_FILE" ]] || \
      bootstrap_die "RCON password path is not a regular file: $RCON_PASSWORD_FILE"
    bootstrap_skip "Preserving canonical RCON password file"
  elif [[ -e "$LEGACY_RCON_PASSWORD_FILE" || -L "$LEGACY_RCON_PASSWORD_FILE" ]]; then
    copy_legacy_rcon_password
  else
    generate_rcon_password
  fi

  run_as_root test -s "$RCON_PASSWORD_FILE" || \
    bootstrap_die "RCON password file is empty: $RCON_PASSWORD_FILE"

  if ! run_as_root chown "$RCON_PASSWORD_OWNER:$RCON_PASSWORD_GROUP" -- "$RCON_PASSWORD_FILE" || \
    ! run_as_root chmod 600 -- "$RCON_PASSWORD_FILE"; then
    bootstrap_die "Failed to restrict permissions on the RCON password file"
  fi

  mode="$(run_as_root stat -c '%a' -- "$RCON_PASSWORD_FILE")"
  [[ "$mode" == "600" ]] || \
    bootstrap_die "RCON password file must have permission mode 0600"
  owner="$(run_as_root stat -c '%U' -- "$RCON_PASSWORD_FILE")"
  group="$(run_as_root stat -c '%G' -- "$RCON_PASSWORD_FILE")"
  [[ "$owner" == "$RCON_PASSWORD_OWNER" && "$group" == "$RCON_PASSWORD_GROUP" ]] || \
    bootstrap_die "RCON password file must be owned by $RCON_PASSWORD_OWNER:$RCON_PASSWORD_GROUP"
}

validate_compose_configuration() {
  local rcon_port

  rcon_port="$(topology_rcon_port)" || return 1
  if ! RUST_SERVER_RCON_PORT="$rcon_port" docker compose \
    --env-file "$RUST_SERVER_ENV_FILE" \
    -f "$RUST_SERVER_COMPOSE_FILE" \
    config --quiet; then
    bootstrap_die "Docker Compose validation failed for canonical Rust server configuration"
  fi
}

require_regular_file "$RUST_SERVER_ENV_TEMPLATE"
require_regular_file "$RUST_SERVER_COMPOSE_FILE"
require_regular_file "$TOPOLOGY_TEMPLATE"

ensure_sudo_access

rust_server_migrate_configuration \
  "$LEGACY_RUST_SERVER_ENV_FILE" \
  "$RUST_SERVER_ENV_FILE" \
  "$RUST_SERVER_ENV_TEMPLATE"
create_topology_file_if_missing
ensure_topology_rcon_grpc_urls
ensure_topology_bind_hosts
load_topology_environment

for required_key in "${REQUIRED_RUST_SERVER_ENV_KEYS[@]}"; do
  require_nonempty_env_key "$required_key"
done

ensure_rcon_password
validate_compose_configuration

bootstrap_info "Canonical Rust server configuration and local RCON secret are valid"
