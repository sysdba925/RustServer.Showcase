#!/usr/bin/env bash
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"
readonly TEST_DIRECTORY="$(mktemp -d "$PLATFORM_DIR/.rust-server-compose-test.XXXXXX")"
trap 'rm -rf -- "$TEST_DIRECTORY"' EXIT

if docker compose version >/dev/null 2>&1; then
  use_windows_paths=0
elif docker.exe compose version >/dev/null 2>&1; then
  use_windows_paths=1
else
  printf 'SKIP: Docker Compose is unavailable; Rust and SteamCMD Compose rendering was not run.\n'
  exit 0
fi

compose_path() {
  local path="$1"

  if (( use_windows_paths )); then
    wslpath -w "$path"
  else
    printf '%s\n' "$path"
  fi
}

render_compose_configuration() {
  local environment_file="$1"
  local compose_file="$2"
  local config_mode="$3"

  if (( use_windows_paths )); then
    powershell.exe -NoProfile -NonInteractive -Command "\$env:RUST_SERVER_RCON_PORT = '28016'; & docker.exe compose --env-file '$(compose_path "$environment_file")' -f '$(compose_path "$compose_file")' config $config_mode"
  else
    RUST_SERVER_RCON_PORT=28016 docker compose \
      --env-file "$environment_file" \
      -f "$compose_file" \
      config $config_mode
  fi
}

readonly RUST_COMPOSE_DIRECTORY="$TEST_DIRECTORY/rust-server"
readonly STEAMCMD_COMPOSE_DIRECTORY="$TEST_DIRECTORY/steamcmd"
readonly RUST_SERVER_ENV_FILE="$TEST_DIRECTORY/rust-server.env"

mkdir -p -- "$RUST_COMPOSE_DIRECTORY/scripts" "$STEAMCMD_COMPOSE_DIRECTORY/scripts"
cp -- "$PLATFORM_DIR/docker/rust-server/compose.yml" "$RUST_COMPOSE_DIRECTORY/compose.yml"
cp -- "$PLATFORM_DIR/docker/rust-server/scripts/start-rust.sh" "$RUST_COMPOSE_DIRECTORY/scripts/start-rust.sh"
cp -- "$PLATFORM_DIR/docker/steamcmd/compose.yml" "$STEAMCMD_COMPOSE_DIRECTORY/compose.yml"
cp -- "$PLATFORM_DIR/docker/steamcmd/scripts/update-rust.sh" "$STEAMCMD_COMPOSE_DIRECTORY/scripts/update-rust.sh"
printf 'test-password\n' > "$RUST_COMPOSE_DIRECTORY/.rcon-password"
cp -- "$PLATFORM_DIR/docker/rust-server/.env.example" "$RUST_SERVER_ENV_FILE"

render_compose_configuration \
  "$RUST_SERVER_ENV_FILE" \
  "$RUST_COMPOSE_DIRECTORY/compose.yml" \
  '--quiet'
render_compose_configuration \
  "$RUST_SERVER_ENV_FILE" \
  "$STEAMCMD_COMPOSE_DIRECTORY/compose.yml" \
  '--quiet'

printf 'Rust and SteamCMD Compose configuration tests passed.\n'
