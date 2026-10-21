#!/usr/bin/env bash
# Validates the immutable Updater release layout consumed by Bootstrap only.

readonly UPDATER_BUNDLE_MANIFEST='rustserver-updater.bundle.json'
readonly UPDATER_API_RELATIVE_PATH='api/RustServer.Updater.Api.dll'
readonly UPDATER_EXECUTOR_RELATIVE_PATH='executor/RustServer.Updater.Executor.dll'
readonly UPDATER_SYSTEMD_DIRECTORY='systemd'
readonly -a UPDATER_SYSTEMD_UNITS=(
  'rustserver-updater.service'
  'rustserver-updater-executor.socket'
  'rustserver-updater-executor@.service'
  'rustserver-updater-recovery.service'
)

updater_bundle_release_directory_is_valid() {
  local release_directory="$1"
  local release_tag="$2"
  local required_path
  local unit_name

  release_tag_is_valid "$release_tag" || return 1
  [[ -d "$release_directory" && ! -L "$release_directory" ]] || return 1
  for required_path in \
    "$UPDATER_BUNDLE_MANIFEST" \
    "$UPDATER_API_RELATIVE_PATH" \
    "$UPDATER_EXECUTOR_RELATIVE_PATH"; do
    [[ -f "$release_directory/$required_path" && ! -L "$release_directory/$required_path" ]] || return 1
  done
  [[ -d "$release_directory/$UPDATER_SYSTEMD_DIRECTORY" && ! -L "$release_directory/$UPDATER_SYSTEMD_DIRECTORY" ]] || return 1
  for unit_name in "${UPDATER_SYSTEMD_UNITS[@]}"; do
    [[ -f "$release_directory/$UPDATER_SYSTEMD_DIRECTORY/$unit_name" && \
      ! -L "$release_directory/$UPDATER_SYSTEMD_DIRECTORY/$unit_name" ]] || return 1
  done

  python3 - "$release_directory/$UPDATER_BUNDLE_MANIFEST" "$release_tag" <<'PY'
import json
import sys

manifest_path, release_tag = sys.argv[1:]
try:
    with open(manifest_path, encoding="utf-8") as manifest_file:
        manifest = json.load(manifest_file)
except (OSError, json.JSONDecodeError):
    raise SystemExit(1)

if not isinstance(manifest, dict):
    raise SystemExit(1)

raise SystemExit(0 if (
    manifest.get("formatVersion") == 1
    and manifest.get("releaseVersion") == release_tag
    and manifest.get("updaterEntrypoint") == "api/RustServer.Updater.Api.dll"
    and manifest.get("executorEntrypoint") == "executor/RustServer.Updater.Executor.dll"
) else 1)
PY
}

require_updater_bundle_release_directory() {
  local release_directory="$1"
  local release_tag="$2"

  updater_bundle_release_directory_is_valid "$release_directory" "$release_tag" || \
    bootstrap_die "Updater release is not a complete bundled API, Executor, and systemd artifact: $release_directory"
}
