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

readonly UPDATER_USER="${RUSTSERVER_UPDATER_SERVICE_USER:-rustserver-updater}"
readonly UPDATER_GROUP="${RUSTSERVER_UPDATER_SERVICE_GROUP:-rustserver-updater}"
readonly UPDATER_CURRENT_LINK='/opt/rustserver/updater/current'

temporary_directory=''

cleanup() {
  local exit_code=$?

  cleanup_github_authentication
  if [[ -n "$temporary_directory" ]]; then
    rm -rf -- "$temporary_directory"
  fi
  exit "$exit_code"
}

create_updater_user_if_missing() {
  if getent passwd "$UPDATER_USER" >/dev/null; then
    return
  fi

  run_as_root useradd --system --user-group --home-dir /var/lib/rustserver/updater \
    --shell /usr/sbin/nologin "$UPDATER_USER" || \
    bootstrap_die "Could not create service user: $UPDATER_USER"
  bootstrap_info "Created dedicated service user: $UPDATER_USER"
}

main() {
  local channel
  local selected_release_tag
  local active_directory=''

  if ! bootstrap_mode_is install; then
    bootstrap_skip "Step is not required for Bootstrap mode: $(bootstrap_mode)"
    return
  fi

  ensure_sudo_access
  create_updater_user_if_missing
  ensure_updater_configuration_directory

  if [[ -e "$UPDATER_CURRENT_LINK" || -L "$UPDATER_CURRENT_LINK" ]]; then
    active_directory="$(updater_current_directory)"
    bootstrap_skip "Active Updater release is preserved: ${active_directory##*/}"
    return
  fi

  if root_path_exists "$UPDATER_RELEASE_CHANNEL_FILE"; then
    channel="$(read_updater_release_channel)"
  else
    channel="$(release_channel_for_initial_installation)"
  fi

  trap cleanup EXIT
  prepare_github_authentication
  temporary_directory="$(mktemp -d)" || \
    bootstrap_die 'Could not create temporary GitHub Releases directory'
  if ! selected_release_tag="$(discover_latest_updater_release "$channel" "$temporary_directory/releases.json")"; then
    bootstrap_die "No valid published RustServer.Updater release is available for the selected $channel channel. The channel was not saved; publish a matching non-draft release or run install again with UPDATER_RELEASE_CHANNEL set explicitly."
  fi

  # Persist only after GitHub has proved that this chosen channel contains an
  # installable release. This leaves a failed first installation retryable.
  create_updater_release_channel "$channel"
  write_initial_updater_release_selection "$channel" "$selected_release_tag"
  bootstrap_info "Selected Updater release channel $channel; latest published release: $selected_release_tag"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
