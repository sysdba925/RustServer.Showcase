#!/usr/bin/env bash
set -euo pipefail

server_binary=/server/RustDedicated
rcon_password_file=/run/secrets/rust_server_rcon_password

if [[ ! -x "$server_binary" ]]; then
  echo "RustDedicated is missing or is not executable at $server_binary." >&2
  exit 1
fi

if [[ ! -f "$rcon_password_file" ]]; then
  echo "RCON password secret file is missing at $rcon_password_file." >&2
  exit 1
fi

if [[ ! -r "$rcon_password_file" ]]; then
  echo "RCON password secret file is not readable at $rcon_password_file." >&2
  exit 1
fi

rcon_password=""
IFS= read -r rcon_password < "$rcon_password_file" || true
rcon_password="${rcon_password%$'\r'}"

if [[ -z "$rcon_password" ]]; then
  echo "RCON password secret file is empty at $rcon_password_file." >&2
  exit 1
fi

arguments=(
  -batchmode
  +server.port "${RUST_SERVER_PORT}"
  +server.queryport "${RUST_SERVER_QUERY_PORT}"
  +rcon.port "${RUST_SERVER_RCON_PORT}"
  +rcon.password "$rcon_password"
  +rcon.web 1
  +server.level "${RUST_SERVER_LEVEL}"
  +server.worldsize "${RUST_SERVER_WORLD_SIZE}"
  +server.maxplayers "${RUST_SERVER_MAX_PLAYERS}"
  +server.hostname "${RUST_SERVER_HOSTNAME}"
  +server.description "${RUST_SERVER_DESCRIPTION}"
  +server.tickrate "${RUST_SERVER_TICK_RATE}"
  +server.saveinterval "${RUST_SERVER_SAVE_INTERVAL}"
  +server.identity "${RUST_SERVER_IDENTITY}"
)

if [[ -n "${RUST_SERVER_SEED}" ]]; then
  arguments+=(+server.seed "${RUST_SERVER_SEED}")
fi

exec "$server_binary" "${arguments[@]}"
