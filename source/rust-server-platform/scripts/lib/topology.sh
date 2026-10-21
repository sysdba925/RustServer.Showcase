#!/usr/bin/env bash
# Shared topology-file parsing for bootstrap and direct development commands.

readonly RUSTSERVER_TOPOLOGY_FILE="${RUSTSERVER_TOPOLOGY_FILE:-/etc/rustserver/topology.env}"
readonly -a RUSTSERVER_TOPOLOGY_KEYS=(
  RUSTSERVER_CONTROL_URL
  RUSTSERVER_VERSIONS_URL
  RUSTSERVER_PLUGINS_URL
  RUSTSERVER_SNAPSHOTS_URL
  RUSTSERVER_WEB_URL
  RUSTSERVER_RCON_URL
  RUSTSERVER_RCON_GRPC_URL
  RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL
  RUSTSERVER_CONTROL_BIND_HOST
  RUSTSERVER_VERSIONS_BIND_HOST
  RUSTSERVER_PLUGINS_BIND_HOST
  RUSTSERVER_SNAPSHOTS_BIND_HOST
  RUSTSERVER_WEB_BIND_HOST
)

topology_error() {
  local message="$1"

  if declare -F bootstrap_die >/dev/null; then
    bootstrap_die "$message"
  fi

  printf 'Error: %s\n' "$message" >&2
  return 1
}

topology_trim() {
  local value="$1"

  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "$value"
}

topology_validate_ipv4_bind_host() {
  local key="$1"
  local value="$2"
  local -a octets
  local octet
  local IFS='.'

  if [[ ! "$value" =~ ^([0-9]+\.){3}[0-9]+$ ]]; then
    topology_error "Topology key '$key' must be a canonical IPv4 bind host"
    return 1
  fi

  read -r -a octets <<< "$value"
  if (( ${#octets[@]} != 4 )); then
    topology_error "Topology key '$key' must be a canonical IPv4 bind host"
    return 1
  fi

  for octet in "${octets[@]}"; do
    if [[ ! "$octet" =~ ^(0|[1-9][0-9]{0,2})$ ]] || (( 10#$octet > 255 )); then
      topology_error "Topology key '$key' must be a canonical IPv4 bind host"
      return 1
    fi
  done
}

topology_validate_loopback_http_url() {
  local key="$1"
  local value="$2"
  local port

  if [[ ! "$value" =~ ^http://(127\.0\.0\.1|localhost|\[::1\]):([1-9][0-9]*)/?$ ]]; then
    topology_error "Topology key '$key' must be an absolute localhost HTTP URL with an explicit TCP port"
    return 1
  fi

  port="${BASH_REMATCH[2]}"
  if (( port > 65535 )); then
    topology_error "Topology key '$key' must contain a TCP port between 1 and 65535"
    return 1
  fi
}

load_topology_environment() {
  local line
  local line_number=0
  local key
  local value
  local -A seen=()

  [[ -f "$RUSTSERVER_TOPOLOGY_FILE" && ! -L "$RUSTSERVER_TOPOLOGY_FILE" ]] || {
    topology_error "Topology file is missing or is not a regular file: $RUSTSERVER_TOPOLOGY_FILE"
    return 1
  }

  while IFS= read -r line || [[ -n "$line" ]]; do
    ((line_number += 1))
    line="$(topology_trim "${line%$'\r'}")"

    [[ -z "$line" || "$line" == \#* ]] && continue

    if [[ ! "$line" =~ ^([A-Za-z_][A-Za-z0-9_]*)=(.*)$ ]]; then
      topology_error "Invalid topology entry on line $line_number of $RUSTSERVER_TOPOLOGY_FILE"
      return 1
    fi

    key="${BASH_REMATCH[1]}"
    value="$(topology_trim "${BASH_REMATCH[2]}")"

    case "$key" in
      # Retain legacy Catalog topology entries byte-for-byte on existing hosts,
      # but do not validate, require, or export them: Catalog now owns its own
      # loopback listener configuration through explicit provisioning.
      RUSTSERVER_PLUGIN_CATALOG_URL|RUSTSERVER_PLUGIN_CATALOG_BIND_HOST)
        continue
        ;;
      RUSTSERVER_CONTROL_URL|RUSTSERVER_VERSIONS_URL|RUSTSERVER_PLUGINS_URL|RUSTSERVER_SNAPSHOTS_URL|RUSTSERVER_WEB_URL|RUSTSERVER_RCON_URL|RUSTSERVER_RCON_GRPC_URL|RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL|RUSTSERVER_CONTROL_BIND_HOST|RUSTSERVER_VERSIONS_BIND_HOST|RUSTSERVER_PLUGINS_BIND_HOST|RUSTSERVER_SNAPSHOTS_BIND_HOST|RUSTSERVER_WEB_BIND_HOST)
        ;;
      *)
        topology_error "Unsupported topology key '$key' on line $line_number of $RUSTSERVER_TOPOLOGY_FILE"
        return 1
        ;;
    esac

    if [[ -z "$value" ]]; then
      topology_error "Topology key '$key' is empty in $RUSTSERVER_TOPOLOGY_FILE"
      return 1
    fi

    case "$key" in
      RUSTSERVER_CONTROL_BIND_HOST|RUSTSERVER_VERSIONS_BIND_HOST|RUSTSERVER_PLUGINS_BIND_HOST|RUSTSERVER_SNAPSHOTS_BIND_HOST|RUSTSERVER_WEB_BIND_HOST)
        topology_validate_ipv4_bind_host "$key" "$value" || return 1
        ;;
      RUSTSERVER_RCON_GRPC_URL|RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL)
        topology_validate_loopback_http_url "$key" "$value" || return 1
        ;;
    esac

    if [[ -n "${seen[$key]:-}" ]]; then
      topology_error "Topology key '$key' is defined more than once in $RUSTSERVER_TOPOLOGY_FILE"
      return 1
    fi

    seen["$key"]=1
    export "$key=$value"
  done < "$RUSTSERVER_TOPOLOGY_FILE"

  for key in "${RUSTSERVER_TOPOLOGY_KEYS[@]}"; do
    if [[ -z "${seen[$key]:-}" ]]; then
      topology_error "Topology key '$key' is required in $RUSTSERVER_TOPOLOGY_FILE"
      return 1
    fi
  done
}

topology_rcon_port() {
  local authority
  local port

  [[ "${RUSTSERVER_RCON_URL:-}" == ws://* ]] || {
    topology_error "RUSTSERVER_RCON_URL must use the ws scheme before Docker Compose can be configured"
    return 1
  }

  authority="${RUSTSERVER_RCON_URL#ws://}"
  authority="${authority%%/*}"
  if [[ "$authority" == \[*\]:* ]]; then
    port="${authority##*:}"
  elif [[ "$authority" == *:* ]]; then
    port="${authority##*:}"
  else
    topology_error "RUSTSERVER_RCON_URL must include an explicit TCP port"
    return 1
  fi

  if [[ ! "$port" =~ ^[1-9][0-9]*$ ]] || (( port > 65535 )); then
    topology_error "RUSTSERVER_RCON_URL must contain a TCP port between 1 and 65535"
    return 1
  fi

  printf '%s\n' "$port"
}
