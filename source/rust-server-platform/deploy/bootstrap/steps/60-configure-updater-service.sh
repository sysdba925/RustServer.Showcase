#!/usr/bin/env bash
set -Eeuo pipefail

STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly STEP_DIR
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"
# shellcheck source=../lib/updater-configuration.sh
source "$STEP_DIR/../lib/updater-configuration.sh"
# shellcheck source=../lib/updater-bundle.sh
source "$STEP_DIR/../lib/updater-bundle.sh"
# shellcheck source=../lib/updater-systemd.sh
source "$STEP_DIR/../lib/updater-systemd.sh"

if ! bootstrap_mode_is install; then
  bootstrap_skip "Step is not required for Bootstrap mode: $(bootstrap_mode)"
  exit 0
fi

readonly UPDATER_USER='rustserver-updater'
readonly UPDATER_GROUP='rustserver-updater'
readonly UPDATER_STATE_DIRECTORY='/var/lib/rustserver/updater'
readonly UPDATER_ENVIRONMENT_FILE='/etc/rustserver/updater.env'

create_updater_user_if_missing() {
  if getent passwd "$UPDATER_USER" >/dev/null; then
    bootstrap_skip "Preserving existing service user: $UPDATER_USER"
    return
  fi

  run_as_root useradd --system --user-group --home-dir "$UPDATER_STATE_DIRECTORY" \
    --shell /usr/sbin/nologin "$UPDATER_USER" || \
    bootstrap_die "Could not create service user: $UPDATER_USER"
  bootstrap_info "Created dedicated service user: $UPDATER_USER"
}

configure_state_directory() {
  require_directory "$UPDATER_STATE_DIRECTORY"
  run_as_root chown "$UPDATER_USER:$UPDATER_GROUP" -- "$UPDATER_STATE_DIRECTORY" || \
    bootstrap_die "Could not assign the Updater state directory to its service user"
  run_as_root chmod 0750 -- "$UPDATER_STATE_DIRECTORY" || \
    bootstrap_die "Could not protect the Updater state directory"
}

main() {
  ensure_sudo_access
  dotnet_has_aspnetcore_runtime_10 || \
    bootstrap_die 'ASP.NET Core Runtime 10 is required before configuring the Updater service'
  require_root_regular_file "$UPDATER_ENVIRONMENT_FILE"

  create_updater_user_if_missing
  configure_state_directory
  install_bundled_updater_systemd_units "$(updater_current_directory)" "$(updater_current_release_tag)"
  write_updater_runtime_dropin
  write_updater_credential_dropin

  run_as_root systemctl daemon-reload || bootstrap_die 'Could not reload bundled Updater systemd units'
  run_as_root systemctl enable --now rustserver-updater-executor.socket || \
    bootstrap_die 'Could not enable the bundled Deployment Executor socket'
  run_as_root systemctl enable --now rustserver-updater.service || \
    bootstrap_die 'Could not enable and start rustserver-updater.service'
  run_as_root systemctl is-active --quiet rustserver-updater.service || \
    bootstrap_die 'rustserver-updater.service is not active after startup'
  bootstrap_info 'Bundled Updater, Executor socket, and recovery service are installed and active'
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
