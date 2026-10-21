#!/usr/bin/env bash
# Protect the canonical RCON password while allowing the fixed SteamCMD
# container user (UID 1000) to read its Docker Compose file-backed secret.
# The ordinary group and other ACL entries must remain inaccessible.

readonly RCON_SECRET_ROOT_ACL=$'user::rw-\ngroup::---\nother::---'
readonly RCON_SECRET_GAME_ACL=$'user::rw-\nuser:1000:r--\ngroup::---\nmask::r--\nother::---'

rcon_secret_acl() {
  run_as_root getfacl -cnp -- "$1" 2>/dev/null
}

rcon_secret_has_expected_game_acl() {
  local acl
  acl="$(rcon_secret_acl "$1")" || return 1
  [[ "$acl" == "$RCON_SECRET_GAME_ACL" ]]
}

rcon_secret_permissions_are_valid() {
  local secret_file="$1"
  local owner_group_mode
  owner_group_mode="$(run_as_root stat -c '%U:%G:%a' -- "$secret_file")" || return 1

  case "$owner_group_mode" in
    root:root:600) return 0 ;;
    root:root:640) rcon_secret_has_expected_game_acl "$secret_file" ;;
    *) return 1 ;;
  esac
}

rcon_secret_grant_game_container_read() {
  local secret_file="$1"
  local owner_group_mode

  require_root_regular_file "$secret_file"
  rcon_secret_permissions_are_valid "$secret_file" || \
    bootstrap_die "RCON password has unexpected owner, mode, or ACL: $secret_file"

  owner_group_mode="$(run_as_root stat -c '%U:%G:%a' -- "$secret_file")"
  if [[ "$owner_group_mode" == 'root:root:600' ]]; then
    # Reject dormant ACL entries: changing the mask must not activate
    # previously ineffective permissions for any other identity.
    local acl
    acl="$(rcon_secret_acl "$secret_file")" || \
      bootstrap_die "Could not read RCON password ACL: $secret_file"
    [[ "$acl" == "$RCON_SECRET_ROOT_ACL" ]] || \
      bootstrap_die "RCON password has unexpected ACL entries: $secret_file"
  fi

  run_as_root setfacl -m 'u:1000:r--' -- "$secret_file" || \
    bootstrap_die "Could not grant the SteamCMD user read access to the RCON password"
  rcon_secret_has_expected_game_acl "$secret_file" || \
    bootstrap_die "RCON password ACL is not restricted to root and SteamCMD UID 1000"
}
