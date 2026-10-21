#!/usr/bin/env bash
set -euo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"
readonly RUNTIME_DIRECTORY="/opt/rustserver/runtime"
readonly RUST_SERVER_ENV_FILE="/etc/rustserver/rust-server.env"
readonly CONTROL_CONFIGURATION_FILE="/var/lib/rustserver/control/server-configuration.json"
readonly RCON_PASSWORD_FILE="/etc/rustserver/rcon-password"
readonly CONTROL_REPOSITORY_DIR="$(cd -- "$PLATFORM_DIR/.." && pwd -P)/repos/RustServer.Control"
# shellcheck source=../lib/rust-server-configuration.sh
source "$SCRIPT_DIR/../lib/rust-server-configuration.sh"
# shellcheck source=../lib/topology.sh
source "$SCRIPT_DIR/../lib/topology.sh"

export ASPNETCORE_ENVIRONMENT=Development
export RustServerControl__RustServerComposePath="$RUNTIME_DIRECTORY/rust-server/compose.yml"
export RustServerUpdate__SteamCmdComposePath="$RUNTIME_DIRECTORY/steamcmd/compose.yml"
export OxideUpdate__OxideComposePath="$RUNTIME_DIRECTORY/oxide/compose.yml"
export ServerConfiguration__StoragePath="$CONTROL_CONFIGURATION_FILE"
load_topology_environment
rust_server_export_environment "$RUST_SERVER_ENV_FILE"

read_compose_env_value() {
  local key="$1"

  rust_server_env_read_value "$RUST_SERVER_ENV_FILE" "$key"
}

read_compose_env_value_or_default() {
  local key="$1"
  local default_value="$2"
  local value

  if value="$(read_compose_env_value "$key")" && [[ -n "$value" ]]; then
    printf '%s\n' "$value"
  else
    printf '%s\n' "$default_value"
  fi
}

rust_server_data_path="$(read_compose_env_value "RUST_SERVER_PATH")" || {
  printf 'Error: RUST_SERVER_PATH is missing from %s\n' "$RUST_SERVER_ENV_FILE" >&2
  exit 1
}
[[ -n "$rust_server_data_path" ]] || {
  printf 'Error: RUST_SERVER_PATH is empty in %s\n' "$RUST_SERVER_ENV_FILE" >&2
  exit 1
}
rust_server_identity="$(read_compose_env_value "RUST_SERVER_IDENTITY")" || {
  printf 'Error: RUST_SERVER_IDENTITY is missing from %s\n' "$RUST_SERVER_ENV_FILE" >&2
  exit 1
}
[[ "$rust_server_identity" =~ ^[A-Za-z0-9_-]+$ ]] || {
  printf 'Error: RUST_SERVER_IDENTITY must contain only letters, digits, underscores, or hyphens\n' >&2
  exit 1
}
export RustServerControl__RustServerDataPath="$rust_server_data_path"
export RustServerControl__RustServerIdentity="$rust_server_identity"
export ServerConfiguration__InitialDesired__Hostname="$(read_compose_env_value_or_default "RUST_SERVER_HOSTNAME" "Rust Server")"
export ServerConfiguration__InitialDesired__Description="$(read_compose_env_value_or_default "RUST_SERVER_DESCRIPTION" "")"
export ServerConfiguration__InitialDesired__MaxPlayers="$(read_compose_env_value_or_default "RUST_SERVER_MAX_PLAYERS" "50")"
export ServerConfiguration__InitialDesired__TickRate="$(read_compose_env_value_or_default "RUST_SERVER_TICK_RATE" "30")"
export ServerConfiguration__InitialDesired__SaveInterval="$(read_compose_env_value_or_default "RUST_SERVER_SAVE_INTERVAL" "600")"
export ServerConfiguration__InitialDesired__Seed="$(read_compose_env_value_or_default "RUST_SERVER_SEED" "0")"
export ServerConfiguration__InitialDesired__WorldSize="$(read_compose_env_value_or_default "RUST_SERVER_WORLD_SIZE" "3500")"

if [[ -z "${RustServerRcon__Password:-}" ]]; then
  rcon_password_file="$RCON_PASSWORD_FILE"

  if [[ ! -f "$rcon_password_file" ]]; then
    printf 'Error: RCON password secret file not found: %s\n' "$rcon_password_file" >&2
    exit 1
  fi

  if [[ ! -r "$rcon_password_file" ]]; then
    printf 'Error: RCON password secret file is not readable: %s\n' "$rcon_password_file" >&2
    exit 1
  fi

  rcon_password=""
  IFS= read -r rcon_password < "$rcon_password_file" || true
  rcon_password="${rcon_password%$'\r'}"

  if [[ -z "$rcon_password" ]]; then
    printf 'Error: RCON password secret file is empty: %s\n' "$rcon_password_file" >&2
    exit 1
  fi

  export RustServerRcon__Password="$rcon_password"
fi

cd "$CONTROL_REPOSITORY_DIR"

exec dotnet run \
  --no-launch-profile \
  --project src/RustServer.Control.Api
