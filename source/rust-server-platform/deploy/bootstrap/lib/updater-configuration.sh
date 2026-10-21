#!/usr/bin/env bash
# Shared helpers for the persistent Updater runtime configuration layout.

readonly UPDATER_CONFIGURATION_DIRECTORY='/etc/rustserver'
readonly UPDATER_REGISTRY_DIRECTORY="$UPDATER_CONFIGURATION_DIRECTORY/updater"
readonly UPDATER_REGISTRY_FILE="$UPDATER_REGISTRY_DIRECTORY/applications.ini"
readonly UPDATER_RELEASE_CHANNEL_FILE="$UPDATER_CONFIGURATION_DIRECTORY/updater-release-channel"
readonly UPDATER_TOKEN_FILE="$UPDATER_CONFIGURATION_DIRECTORY/updater-github-token"
readonly UPDATER_SERVICE_DROPIN_DIRECTORY='/etc/systemd/system/rustserver-updater.service.d'
readonly UPDATER_CREDENTIAL_DROPIN_FILE="$UPDATER_SERVICE_DROPIN_DIRECTORY/credentials.conf"

ensure_updater_configuration_directory() {
  if root_path_exists "$UPDATER_CONFIGURATION_DIRECTORY"; then
    require_directory "$UPDATER_CONFIGURATION_DIRECTORY"
  else
    run_as_root install -d -m 0755 -o root -g root -- "$UPDATER_CONFIGURATION_DIRECTORY" || \
      bootstrap_die 'Could not create the Updater configuration directory'
  fi

  # Development and Production use separate service accounts. Individual
  # configuration files carry the restrictive access policy.
  run_as_root chown root:root -- "$UPDATER_CONFIGURATION_DIRECTORY" || \
    bootstrap_die 'Could not protect the Updater configuration directory'
  run_as_root chmod 0755 -- "$UPDATER_CONFIGURATION_DIRECTORY" || \
    bootstrap_die 'Could not protect the Updater configuration directory'

  if root_path_exists "$UPDATER_REGISTRY_DIRECTORY"; then
    require_directory "$UPDATER_REGISTRY_DIRECTORY"
  else
    run_as_root install -d -m 0755 -o root -g root -- "$UPDATER_REGISTRY_DIRECTORY" || \
      bootstrap_die 'Could not create the Updater application registry directory'
  fi
  run_as_root chown root:root -- "$UPDATER_REGISTRY_DIRECTORY" || \
    bootstrap_die 'Could not protect the Updater application registry directory'
  run_as_root chmod 0755 -- "$UPDATER_REGISTRY_DIRECTORY" || \
    bootstrap_die 'Could not protect the Updater application registry directory'
}

create_updater_configuration_file_without_overwrite() {
  local target_file="$1"
  local mode="$2"
  local owner="$3"
  local group="$4"
  local temporary_file=''

  if root_path_exists "$target_file"; then
    require_root_regular_file "$target_file"
    bootstrap_skip "Preserving existing persistent configuration: $target_file"
    return 0
  fi

  temporary_file="$(run_as_root mktemp --tmpdir="$(dirname -- "$target_file")" ".$(basename -- "$target_file").tmp.XXXXXX")" || \
    bootstrap_die 'Could not create temporary persistent configuration'
  if ! cat | run_as_root tee "$temporary_file" >/dev/null; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not write temporary persistent configuration'
  fi
  run_as_root chown "$owner:$group" -- "$temporary_file"
  run_as_root chmod "$mode" -- "$temporary_file"
  if ! run_as_root ln -- "$temporary_file" "$target_file"; then
    run_as_root rm -f -- "$temporary_file"
    if root_path_exists "$target_file"; then
      require_root_regular_file "$target_file"
      bootstrap_skip "Preserving concurrently created persistent configuration: $target_file"
      return 0
    fi
    bootstrap_die "Could not create persistent configuration: $target_file"
  fi
  run_as_root rm -f -- "$temporary_file"
  bootstrap_info "Created persistent configuration: $target_file"
}

read_updater_release_channel() {
  local channel

  channel="$(read_root_regular_file "$UPDATER_RELEASE_CHANNEL_FILE")"
  validate_release_channel "$channel"
  printf '%s\n' "$channel"
}

create_updater_release_channel() {
  local channel="$1"

  validate_release_channel "$channel"
  if root_path_exists "$UPDATER_RELEASE_CHANNEL_FILE"; then
    # Reading and validating the retained value prevents a malformed
    # administrator file from silently changing the selected release stream.
    read_updater_release_channel >/dev/null
    bootstrap_skip "Preserving existing persistent configuration: $UPDATER_RELEASE_CHANNEL_FILE"
    return
  fi

  [[ -n "${UPDATER_GROUP:-}" ]] || \
    bootstrap_die 'Updater service group is required to create the release channel file'
  printf '%s\n' "$channel" | \
    create_updater_configuration_file_without_overwrite "$UPDATER_RELEASE_CHANNEL_FILE" 0640 root "$UPDATER_GROUP"
}

render_updater_credential_dropin() {
  printf '%s\n' \
    '[Service]' \
    'LoadCredential=github-token:/etc/rustserver/updater-github-token' \
    'Environment=RUSTSERVER_UPDATER_GITHUB_TOKEN_FILE=%d/github-token' \
    'ProtectSystem=strict' \
    '# applications.ini is root-owned configuration; state lives under /var/lib.' \
    'ReadWritePaths=/var/lib/rustserver/updater' \
    'InaccessiblePaths=/etc/rustserver/updater-github-token'
}

write_updater_credential_dropin() {
  local temporary_file

  run_as_root install -d -m 0755 -o root -g root -- "$UPDATER_SERVICE_DROPIN_DIRECTORY" || \
    bootstrap_die "Could not create the Updater service drop-in directory"
  temporary_file="$(run_as_root mktemp --tmpdir="$UPDATER_SERVICE_DROPIN_DIRECTORY" '.credentials.conf.tmp.XXXXXX')" || \
    bootstrap_die "Could not create a temporary Updater credential drop-in"
  if ! render_updater_credential_dropin | run_as_root tee "$temporary_file" >/dev/null; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die "Could not write the temporary Updater credential drop-in"
  fi
  run_as_root chmod 0644 -- "$temporary_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die "Could not protect the temporary Updater credential drop-in"
  }
  if ! run_as_root mv -Tf -- "$temporary_file" "$UPDATER_CREDENTIAL_DROPIN_FILE"; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die "Could not install Updater credential drop-in: $UPDATER_CREDENTIAL_DROPIN_FILE"
  fi
  bootstrap_info "Configured Updater credential drop-in: $UPDATER_CREDENTIAL_DROPIN_FILE"
}
