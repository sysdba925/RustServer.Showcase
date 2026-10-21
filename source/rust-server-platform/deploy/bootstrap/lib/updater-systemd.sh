#!/usr/bin/env bash
# Installs systemd resources supplied by an immutable Updater bundle.

updater_systemd_unit_directory() {
  printf '%s\n' "${RUSTSERVER_UPDATER_SYSTEMD_UNIT_DIRECTORY:-/etc/systemd/system}"
}

install_updater_systemd_unit_from_bundle() {
  local release_directory="$1"
  local unit_name="$2"
  local unit_directory
  local source
  local destination
  local temporary=''

  unit_directory="$(updater_systemd_unit_directory)"
  source="$release_directory/$UPDATER_SYSTEMD_DIRECTORY/$unit_name"
  destination="$unit_directory/$unit_name"
  [[ -f "$source" && ! -L "$source" ]] || \
    bootstrap_die "Bundled systemd unit is missing or unsafe: $source"
  require_directory "$unit_directory"
  temporary="$(run_as_root mktemp --tmpdir="$unit_directory" ".${unit_name}.tmp.XXXXXX")" || \
    bootstrap_die "Could not create a temporary systemd unit: $unit_name"
  if ! run_as_root install -m 0644 -- "$source" "$temporary"; then
    run_as_root rm -f -- "$temporary"
    bootstrap_die "Could not stage bundled systemd unit: $unit_name"
  fi
  if ! run_as_root mv -Tf -- "$temporary" "$destination"; then
    run_as_root rm -f -- "$temporary"
    bootstrap_die "Could not install bundled systemd unit: $unit_name"
  fi
}

install_bundled_updater_systemd_units() {
  local release_directory="$1"
  local release_tag="$2"
  local unit_name

  require_updater_bundle_release_directory "$release_directory" "$release_tag"
  for unit_name in "${UPDATER_SYSTEMD_UNITS[@]}"; do
    install_updater_systemd_unit_from_bundle "$release_directory" "$unit_name"
  done
}

write_updater_runtime_dropin() {
  local runtime_dropin="$UPDATER_SERVICE_DROPIN_DIRECTORY/runtime.conf"
  local temporary=''

  run_as_root install -d -m 0755 -o root -g root -- "$UPDATER_SERVICE_DROPIN_DIRECTORY" || \
    bootstrap_die 'Could not create the Updater service drop-in directory'
  temporary="$(run_as_root mktemp --tmpdir="$UPDATER_SERVICE_DROPIN_DIRECTORY" '.runtime.conf.tmp.XXXXXX')" || \
    bootstrap_die 'Could not create a temporary Updater runtime drop-in'
  if ! printf '%s\n' \
    '[Unit]' \
    'Requires=rustserver-updater-recovery.service' \
    'After=rustserver-updater-recovery.service' \
    '' \
    '[Service]' \
    'EnvironmentFile=/etc/rustserver/updater.env' \
    'ExecStart=' \
    "ExecStart=/bin/bash -ec 'exec /usr/bin/dotnet /opt/rustserver/updater/current/$UPDATER_API_RELATIVE_PATH --urls=http://\$\${RUSTSERVER_UPDATER_BIND_ADDRESS}:\$\${RUSTSERVER_UPDATER_PORT}'" | \
    run_as_root tee "$temporary" >/dev/null; then
    run_as_root rm -f -- "$temporary"
    bootstrap_die 'Could not write the Updater runtime drop-in'
  fi
  run_as_root chmod 0644 -- "$temporary" || {
    run_as_root rm -f -- "$temporary"
    bootstrap_die 'Could not protect the Updater runtime drop-in'
  }
  if ! run_as_root mv -Tf -- "$temporary" "$runtime_dropin"; then
    run_as_root rm -f -- "$temporary"
    bootstrap_die 'Could not install the Updater runtime drop-in'
  fi
}
