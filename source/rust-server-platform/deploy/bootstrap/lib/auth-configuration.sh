#!/usr/bin/env bash
# Shared validation and create-only provisioning for the canonical Auth
# environment. Values are deliberately never written to Bootstrap output.

readonly AUTH_ENVIRONMENT_REQUIRED_KEYS=(
  GOOGLE_CLIENT_ID
  GOOGLE_CLIENT_SECRET
  ADMIN_USERNAME
  ADMIN_PASSWORD
)

AUTH_CONFIGURATION_ERROR=''
AUTH_PROVISION_GOOGLE_CLIENT_ID=''
AUTH_PROVISION_GOOGLE_CLIENT_SECRET=''
AUTH_PROVISION_ADMIN_USERNAME=''
AUTH_PROVISION_ADMIN_PASSWORD=''

auth_configuration_fail() {
  AUTH_CONFIGURATION_ERROR="$1"
  return 1
}

require_private_auth_handoff_file() {
  local handoff_file="$1"
  local mode

  [[ -f "$handoff_file" && ! -L "$handoff_file" ]] || {
    auth_configuration_fail 'Auth credential handoff file is missing or unsafe'
    return 1
  }
  mode="$(stat -c '%a' -- "$handoff_file")" || {
    auth_configuration_fail 'Auth credential handoff file permissions could not be inspected'
    return 1
  }
  (( (8#$mode & 077) == 0 )) || {
    auth_configuration_fail 'Auth credential handoff file must not be readable by group or other users'
    return 1
  }
}

read_auth_provisioning_handoff() {
  local handoff_file="$1"
  local line
  local key
  local value
  local -A seen_keys=()

  AUTH_PROVISION_GOOGLE_CLIENT_ID=''
  AUTH_PROVISION_GOOGLE_CLIENT_SECRET=''
  AUTH_PROVISION_ADMIN_USERNAME=''
  AUTH_PROVISION_ADMIN_PASSWORD=''
  require_private_auth_handoff_file "$handoff_file" || return 1

  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ "$line" == *=* ]] || {
      auth_configuration_fail 'Auth credential handoff contains an invalid line'
      return 1
    }
    key="${line%%=*}"
    value="${line#*=}"
    [[ -z "${seen_keys[$key]:-}" ]] || {
      auth_configuration_fail "Auth credential handoff defines $key more than once"
      return 1
    }
    seen_keys["$key"]=1

    case "$key" in
      GOOGLE_CLIENT_ID)
        AUTH_PROVISION_GOOGLE_CLIENT_ID="$value"
        ;;
      GOOGLE_CLIENT_SECRET)
        AUTH_PROVISION_GOOGLE_CLIENT_SECRET="$value"
        ;;
      ADMIN_USERNAME)
        AUTH_PROVISION_ADMIN_USERNAME="$value"
        ;;
      ADMIN_PASSWORD)
        AUTH_PROVISION_ADMIN_PASSWORD="$value"
        ;;
      *)
        auth_configuration_fail "Auth credential handoff contains an unsupported key: $key"
        return 1
        ;;
    esac
  done < "$handoff_file"

  for key in "${AUTH_ENVIRONMENT_REQUIRED_KEYS[@]}"; do
    case "$key" in
      GOOGLE_CLIENT_ID) value="$AUTH_PROVISION_GOOGLE_CLIENT_ID" ;;
      GOOGLE_CLIENT_SECRET) value="$AUTH_PROVISION_GOOGLE_CLIENT_SECRET" ;;
      ADMIN_USERNAME) value="$AUTH_PROVISION_ADMIN_USERNAME" ;;
      ADMIN_PASSWORD) value="$AUTH_PROVISION_ADMIN_PASSWORD" ;;
    esac
    [[ -n "$value" ]] || {
      auth_configuration_fail "Auth credential handoff is missing required variable $key"
      return 1
    }
  done

  (( ${#AUTH_PROVISION_ADMIN_PASSWORD} >= 12 )) || {
    auth_configuration_fail 'Auth initial admin password does not meet the production password policy'
    return 1
  }
}

validate_auth_environment_contract() {
  local environment_file="$1"
  local auth_group="$2"
  local database_file="$3"
  local owner
  local group
  local mode
  local validation_error

  AUTH_CONFIGURATION_ERROR=''
  if ! run_as_root test -f "$environment_file" || run_as_root test -L "$environment_file"; then
    auth_configuration_fail 'Auth environment file is missing or unsafe'
    return 1
  fi
  owner="$(run_as_root stat -c '%U' -- "$environment_file")" || {
    auth_configuration_fail 'Auth environment ownership could not be inspected'
    return 1
  }
  group="$(run_as_root stat -c '%G' -- "$environment_file")" || {
    auth_configuration_fail 'Auth environment group could not be inspected'
    return 1
  }
  mode="$(run_as_root stat -c '%a' -- "$environment_file")" || {
    auth_configuration_fail 'Auth environment permissions could not be inspected'
    return 1
  }
  if [[ "$owner" != root || "$group" != "$auth_group" || "$mode" != 640 ]]; then
    auth_configuration_fail 'Auth environment must be owned by root:rustserver-auth with mode 0640'
    return 1
  fi

  if ! validation_error="$(run_as_root python3 - "$environment_file" "$database_file" <<'PY'
import os
import pathlib
import sqlite3
import sys

environment_path, database_path = sys.argv[1:]
required = {"GOOGLE_CLIENT_ID", "GOOGLE_CLIENT_SECRET"}
initialization = {"ADMIN_USERNAME", "ADMIN_PASSWORD", "Bootstrap__AdminUserName"}
allowed = required | initialization | {"Auth__AllowInsecureHttp"}
values = {}

try:
    with open(environment_path, encoding="utf-8") as source:
        for line_number, raw_line in enumerate(source, 1):
            line = raw_line.rstrip("\r\n")
            if not line or line.startswith("#"):
                continue
            if "=" not in line:
                raise ValueError(f"invalid syntax at line {line_number}")
            key, value = line.split("=", 1)
            if key not in allowed:
                raise ValueError(f"unsupported variable {key}")
            if key in values:
                raise ValueError(f"defines {key} more than once")
            values[key] = value
except (OSError, UnicodeError, ValueError) as error:
    raise SystemExit(f"Auth environment cannot be validated: {error}")

def is_nonempty(value):
    return value is not None and value not in {"", "''", '\"\"'} and not value.startswith("replace-with-")

def unquote(value):
    if len(value) >= 2 and value[0] == value[-1] and value[0] in {"'", '\"'}:
        return value[1:-1]
    return value

missing = sorted(key for key in required if not is_nonempty(values.get(key)))
if missing:
    raise SystemExit(f"Auth environment is missing required variable {missing[0]}")

needs_initial_admin = not os.path.exists(database_path)
if not needs_initial_admin:
    try:
        uri = pathlib.Path(database_path).resolve().as_uri() + "?mode=ro"
        with sqlite3.connect(uri, uri=True) as connection:
            table = connection.execute(
                "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'AspNetUsers'").fetchone()
            needs_initial_admin = table is None
            if not needs_initial_admin:
                needs_initial_admin = connection.execute(
                    "SELECT NOT EXISTS (SELECT 1 FROM AspNetUsers)").fetchone()[0] == 1
    except (OSError, sqlite3.Error) as error:
        raise SystemExit(f"Auth database cannot be inspected without modification: {error}")

if needs_initial_admin:
    missing = sorted(key for key in {"ADMIN_USERNAME", "ADMIN_PASSWORD"} if not is_nonempty(values.get(key)))
    if missing:
        raise SystemExit(f"Auth initial database configuration is missing required variable {missing[0]}")
    if len(unquote(values["ADMIN_PASSWORD"])) < 12:
        raise SystemExit("Auth initial database password does not meet the production password policy")
    bootstrap_name = values.get("Bootstrap__AdminUserName")
    if is_nonempty(bootstrap_name) and unquote(bootstrap_name) != unquote(values["ADMIN_USERNAME"]):
        raise SystemExit("Auth initial admin username does not match Bootstrap__AdminUserName")
PY
  )"; then
    auth_configuration_fail "${validation_error:-Auth environment could not be validated}"
    return 1
  fi
}

escape_systemd_environment_value() {
  local value="$1"

  if [[ ! "$value" =~ [[:space:]\\\"\$\`] ]]; then
    printf '%s' "$value"
    return
  fi
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  value="${value//\$/\\\$}"
  value="${value//\`/\\\`}"
  printf '"%s"' "$value"
}

write_auth_environment() {
  local google_client_id
  local google_client_secret
  local admin_username
  local admin_password

  google_client_id="$(escape_systemd_environment_value "$AUTH_PROVISION_GOOGLE_CLIENT_ID")"
  google_client_secret="$(escape_systemd_environment_value "$AUTH_PROVISION_GOOGLE_CLIENT_SECRET")"
  admin_username="$(escape_systemd_environment_value "$AUTH_PROVISION_ADMIN_USERNAME")"
  admin_password="$(escape_systemd_environment_value "$AUTH_PROVISION_ADMIN_PASSWORD")"
  printf '%s\n' \
    "GOOGLE_CLIENT_ID=$google_client_id" \
    "GOOGLE_CLIENT_SECRET=$google_client_secret" \
    "ADMIN_USERNAME=$admin_username" \
    "ADMIN_PASSWORD=$admin_password"
}

create_auth_environment_without_overwrite() {
  local environment_file="$1"
  local auth_group="$2"
  local database_file="$3"
  local handoff_file="$4"
  local environment_directory
  local temporary_file

  if root_path_exists "$environment_file"; then
    validate_auth_environment_contract "$environment_file" "$auth_group" "$database_file" || \
      bootstrap_die "Existing Auth environment is invalid: $AUTH_CONFIGURATION_ERROR"
    bootstrap_skip "Preserving authoritative Auth environment: $environment_file"
    return
  fi

  [[ -n "$handoff_file" ]] || \
    bootstrap_die "Auth environment is missing; rerun the Production Installer interactively to provision $environment_file"
  read_auth_provisioning_handoff "$handoff_file" || \
    bootstrap_die "Auth credential handoff is invalid: $AUTH_CONFIGURATION_ERROR"

  environment_directory="$(dirname -- "$environment_file")"
  require_directory "$environment_directory"
  temporary_file="$(run_as_root mktemp --tmpdir="$environment_directory" ".$(basename -- "$environment_file").tmp.XXXXXX")" || \
    bootstrap_die 'Could not create a temporary Auth environment file'
  if ! write_auth_environment | run_as_root tee "$temporary_file" >/dev/null; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not write the temporary Auth environment file'
  fi
  run_as_root chown "root:$auth_group" -- "$temporary_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not set ownership on the temporary Auth environment file'
  }
  run_as_root chmod 0640 -- "$temporary_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not set permissions on the temporary Auth environment file'
  }

  # link(2) is an atomic create-only publish: a concurrent operator-created
  # file is never replaced.
  if ! run_as_root ln -- "$temporary_file" "$environment_file"; then
    run_as_root rm -f -- "$temporary_file"
    if root_path_exists "$environment_file"; then
      validate_auth_environment_contract "$environment_file" "$auth_group" "$database_file" || \
        bootstrap_die "Existing Auth environment is invalid: $AUTH_CONFIGURATION_ERROR"
      bootstrap_skip "Preserving concurrently created Auth environment: $environment_file"
      return
    fi
    bootstrap_die "Could not create Auth environment: $environment_file"
  fi
  run_as_root rm -f -- "$temporary_file"
  validate_auth_environment_contract "$environment_file" "$auth_group" "$database_file" || \
    bootstrap_die "Created Auth environment is invalid: $AUTH_CONFIGURATION_ERROR"
  bootstrap_info "Created persistent Auth environment: $environment_file"
}
