#!/usr/bin/env bash
set -Eeuo pipefail

readonly server_path="/srv/rust/server"

if [[ "${1:-}" != "--yes" ]]; then
  echo "This DEVELOPMENT script deletes all contents of: $server_path"
  echo "Run again with:"
  echo "  $0 --yes"
  exit 2
fi

if [[ ! -d "$server_path" ]]; then
  echo "Server directory does not exist: $server_path"
  exit 0
fi

if [[ -x "$server_path/RustDedicated" ]]; then
  echo "Rust server installation found: $server_path/RustDedicated"
else
  echo "RustDedicated was not found, but the directory may still contain files."
fi

echo "Deleting contents of $server_path ..."

find "$server_path" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +

if find "$server_path" -mindepth 1 -print -quit | grep -q .; then
  echo "ERROR: server directory is not empty after reset." >&2
  exit 1
fi

echo "Done. Directory preserved: $server_path"
