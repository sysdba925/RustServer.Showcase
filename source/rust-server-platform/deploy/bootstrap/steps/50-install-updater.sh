#!/usr/bin/env bash
set -Eeuo pipefail

STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly STEP_DIR
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"
# shellcheck source=../lib/updater-configuration.sh
source "$STEP_DIR/../lib/updater-configuration.sh"
# shellcheck source=../lib/updater-release.sh
source "$STEP_DIR/../lib/updater-release.sh"
# shellcheck source=../lib/updater-bundle.sh
source "$STEP_DIR/../lib/updater-bundle.sh"

readonly UPDATER_DIRECTORY="/opt/rustserver/updater"
readonly UPDATER_RELEASES_DIRECTORY="$UPDATER_DIRECTORY/releases"
readonly UPDATER_CURRENT_LINK="$UPDATER_DIRECTORY/current"

github_curl_config=""
temporary_directory=""
release_staging_directory=""

cleanup_release_download_artifacts() {
  cleanup_github_authentication
  if [[ -n "$temporary_directory" ]]; then
    rm -rf -- "$temporary_directory"
    temporary_directory=""
  fi
  if [[ -n "$release_staging_directory" && -d "$release_staging_directory" ]]; then
    run_as_root rm -rf -- "$release_staging_directory" || true
    release_staging_directory=""
  fi
}

cleanup() {
  local exit_code=$?

  cleanup_release_download_artifacts
  exit "$exit_code"
}

checksum_for_asset() {
  local checksum_file="$1"
  local asset_name="$2"
  local line
  local hash
  local listed_name
  local matches=0
  local selected_hash=""

  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ -z "$line" || "$line" == \#* ]] && continue

    if [[ "$line" =~ ^([A-Fa-f0-9]{64})[[:space:]]+\*?([^[:space:]]+)$ ]]; then
      hash="${BASH_REMATCH[1]}"
      listed_name="${BASH_REMATCH[2]}"
      if [[ "$listed_name" == "$asset_name" ]]; then
        selected_hash="$hash"
        matches=$((matches + 1))
      fi
    elif [[ "$line" =~ ^([A-Fa-f0-9]{64})$ ]]; then
      selected_hash="${BASH_REMATCH[1]}"
      matches=$((matches + 1))
    fi
  done < "$checksum_file"

  (( matches == 1 )) || \
    bootstrap_die "Checksum asset must contain exactly one SHA-256 for $asset_name"
  printf '%s\n' "$selected_hash"
}

verify_archive_checksum() {
  local archive="$1"
  local checksum_file="$2"
  local expected_hash
  local actual_hash

  expected_hash="$(checksum_for_asset "$checksum_file" "$RUSTSERVER_UPDATER_ASSET")"
  actual_hash="$(sha256sum -- "$archive" | awk '{print $1}')"
  [[ "$actual_hash" == "$expected_hash" ]] || \
    bootstrap_die "SHA-256 verification failed for $RUSTSERVER_UPDATER_ASSET"
  bootstrap_info "Verified SHA-256 for $RUSTSERVER_UPDATER_ASSET"
}

validate_archive() {
  local archive="$1"
  local entry
  local original_entry
  local component
  local file_type
  local -a archive_components=()

  tar -tzf "$archive" >/dev/null || \
    bootstrap_die "Updater archive cannot be listed as a gzip tar archive"

  while IFS= read -r entry; do
    original_entry="$entry"
    [[ -n "$entry" && "$entry" != /* && "$entry" != *//* ]] || \
      bootstrap_die "Updater archive contains an unsafe absolute or empty path"
    while [[ "$entry" == ./* ]]; do
      entry="${entry#./}"
    done
    # GNU tar commonly records the archive root as './'. It is harmless and
    # does not name a file to extract.
    [[ -z "$entry" ]] && continue
    [[ "$entry" != */ ]] || entry="${entry%/}"
    IFS='/' read -r -a archive_components <<< "$entry"
    for component in "${archive_components[@]}"; do
      [[ -n "$component" && "$component" != '.' && "$component" != '..' ]] || \
        bootstrap_die "Updater archive contains an unsafe path: $original_entry"
    done
  done < <(tar -tzf "$archive")

  while IFS= read -r file_type; do
    case "${file_type:0:1}" in
      -|d)
        ;;
      *)
        bootstrap_die "Updater archive contains unsupported link or special file entries"
        ;;
    esac
  done < <(tar -tvzf "$archive")
}

release_directory_source_from_staging() {
  local staging_directory="$1"
  local -a top_level_entries=()

  mapfile -t top_level_entries < <(find "$staging_directory" -mindepth 1 -maxdepth 1 -printf '%p\n')
  if [[ -f "$staging_directory/$UPDATER_BUNDLE_MANIFEST" ]]; then
    printf '%s\n' "$staging_directory"
  elif (( ${#top_level_entries[@]} == 1 )) && [[ -d "${top_level_entries[0]}" && -f "${top_level_entries[0]}/$UPDATER_BUNDLE_MANIFEST" ]]; then
    printf '%s\n' "${top_level_entries[0]}"
  else
    bootstrap_die "Updater archive must contain $UPDATER_BUNDLE_MANIFEST at its root or in one top-level directory"
  fi
}

stage_archive() {
  local archive="$1"

  release_staging_directory="$(run_as_root mktemp -d --tmpdir="$UPDATER_RELEASES_DIRECTORY" ".${RUSTSERVER_UPDATER_RELEASE_TAG}.tmp.XXXXXX")" || \
    bootstrap_die "Could not create a release staging directory"
  run_as_root chown "$(id -u):$(id -g)" -- "$release_staging_directory" || \
    bootstrap_die "Could not prepare the release staging directory"

  tar --extract --gzip --file "$archive" --directory "$release_staging_directory" \
    --no-same-owner --no-same-permissions || \
    bootstrap_die "Could not extract the verified Updater archive"

  release_directory_source="$(release_directory_source_from_staging "$release_staging_directory")"
  require_updater_bundle_release_directory "$release_directory_source" "$RUSTSERVER_UPDATER_RELEASE_TAG"

  run_as_root chown -R root:root -- "$release_staging_directory" || \
    bootstrap_die "Could not secure the staged Updater release"
  run_as_root chmod -R a+rX -- "$release_staging_directory" || \
    bootstrap_die "Could not set read permissions on the staged Updater release"
}

activate_existing_release() {
  local version_directory="$1"

  [[ -d "$version_directory" && ! -L "$version_directory" ]] || \
    bootstrap_die "Updater release directory is missing or unsafe: $version_directory"
  require_updater_bundle_release_directory "$version_directory" "$RUSTSERVER_UPDATER_RELEASE_TAG"

  run_as_root ln -s "releases/$RUSTSERVER_UPDATER_RELEASE_TAG" "$UPDATER_CURRENT_LINK" || \
    bootstrap_die "Could not create the active Updater release link"
  bootstrap_info "Activated Updater release: $RUSTSERVER_UPDATER_RELEASE_TAG"
}

has_active_updater_release() {
  [[ -e "$UPDATER_CURRENT_LINK" || -L "$UPDATER_CURRENT_LINK" ]]
}

install_release() {
  local release_channel="${1:-}"
  local release_metadata
  local archive_url
  local checksum_url
  local version_directory="$UPDATER_RELEASES_DIRECTORY/$RUSTSERVER_UPDATER_RELEASE_TAG"

  if [[ -d "$version_directory" || -L "$version_directory" ]]; then
    [[ -d "$version_directory" && ! -L "$version_directory" ]] || \
      bootstrap_die "Updater release directory is missing or unsafe: $version_directory"
    require_updater_bundle_release_directory "$version_directory" "$RUSTSERVER_UPDATER_RELEASE_TAG"
    return
  fi

  if [[ -z "$github_curl_config" ]]; then
    prepare_github_authentication
  fi
  if [[ -z "$temporary_directory" ]]; then
    temporary_directory="$(mktemp -d)" || \
      bootstrap_die 'Could not create temporary Updater download directory'
  fi
  release_metadata="$temporary_directory/release.json"
  github_api_get "$UPDATER_RELEASES_API_URL/tags/$RUSTSERVER_UPDATER_RELEASE_TAG" \
    "$release_metadata" "application/vnd.github+json"
  validate_requested_updater_release_metadata "$release_metadata" "$RUSTSERVER_UPDATER_RELEASE_TAG" "$release_channel" || \
    bootstrap_die "GitHub release metadata is invalid for $RUSTSERVER_UPDATER_RELEASE_TAG"

  archive_url="$(release_asset_api_url "$release_metadata" "$RUSTSERVER_UPDATER_ASSET")" || \
    bootstrap_die "Could not find Updater archive '$RUSTSERVER_UPDATER_ASSET' in release $RUSTSERVER_UPDATER_RELEASE_TAG"
  checksum_url="$(release_asset_api_url "$release_metadata" "$RUSTSERVER_UPDATER_CHECKSUM_ASSET")" || \
    bootstrap_die "Could not find checksum asset '$RUSTSERVER_UPDATER_CHECKSUM_ASSET' in release $RUSTSERVER_UPDATER_RELEASE_TAG"

  github_api_get "$archive_url" "$temporary_directory/$RUSTSERVER_UPDATER_ASSET" "application/octet-stream"
  github_api_get "$checksum_url" "$temporary_directory/$RUSTSERVER_UPDATER_CHECKSUM_ASSET" "application/octet-stream"
  verify_archive_checksum "$temporary_directory/$RUSTSERVER_UPDATER_ASSET" "$temporary_directory/$RUSTSERVER_UPDATER_CHECKSUM_ASSET"
  validate_archive "$temporary_directory/$RUSTSERVER_UPDATER_ASSET"
  stage_archive "$temporary_directory/$RUSTSERVER_UPDATER_ASSET"

  # mv within releases is an atomic directory rename. It refuses to replace an
  # existing version, so a concurrent or failed installation cannot affect it.
  if ! run_as_root mv -T -- "$release_directory_source" "$version_directory"; then
    bootstrap_die "Could not install Updater release without replacing an existing version"
  fi
  if [[ -d "$release_staging_directory" ]]; then
    run_as_root rmdir -- "$release_staging_directory" || \
      bootstrap_die "Could not remove the empty Updater release staging directory"
  fi
  release_staging_directory=""

  bootstrap_info "Installed Updater release: $RUSTSERVER_UPDATER_RELEASE_TAG"
}

main() {
  local active_directory
  local release_channel
  local selected_release_tag
  local version_directory

  if ! bootstrap_mode_is install; then
    bootstrap_skip "Step is not required for Bootstrap mode: $(bootstrap_mode)"
    return
  fi

  ensure_sudo_access
  load_bootstrap_defaults
  validate_updater_configuration
  require_directory "$UPDATER_RELEASES_DIRECTORY"

  # An existing current link is the explicit installed version. Bootstrap never
  # changes it during a rerun and never contacts GitHub for an implicit update.
  if has_active_updater_release; then
    active_directory="$(updater_current_directory)"
    bootstrap_skip "Active Updater release is preserved: ${active_directory##*/}"
    return
  fi

  trap cleanup EXIT
  release_channel="$(read_updater_release_channel)"
  read_initial_updater_release_selection
  [[ "$UPDATER_INITIAL_RELEASE_CHANNEL" == "$release_channel" ]] || \
    bootstrap_die 'Initial Updater release selection channel does not match the persistent channel; rerun Bootstrap install'
  selected_release_tag="$UPDATER_INITIAL_RELEASE_TAG"
  set_updater_release_assets "$selected_release_tag"
  validate_updater_release_configuration
  version_directory="$UPDATER_RELEASES_DIRECTORY/$RUSTSERVER_UPDATER_RELEASE_TAG"
  install_release "$release_channel"
  activate_existing_release "$version_directory"
  remove_initial_updater_release_selection
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
