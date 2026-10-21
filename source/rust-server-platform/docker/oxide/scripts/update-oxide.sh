#!/bin/sh
set -eu

readonly oxide_download_url="https://github.com/OxideMod/Oxide.Rust/releases/latest/download/Oxide.Rust-linux.zip"
readonly temporary_directory="$(mktemp -d)"
readonly archive_path="$temporary_directory/Oxide.Rust-linux.zip"

cleanup() {
  rm -rf -- "$temporary_directory"
}

trap cleanup EXIT HUP INT TERM

test -d /server

curl \
  --fail \
  --location \
  --retry 3 \
  --output "$archive_path" \
  "$oxide_download_url"

unzip -tq "$archive_path"
unzip -oq "$archive_path" -d /server
