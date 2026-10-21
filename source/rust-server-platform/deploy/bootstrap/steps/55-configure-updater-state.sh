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

if ! bootstrap_mode_is install; then
  bootstrap_skip "Step is not required for Bootstrap mode: $(bootstrap_mode)"
  exit 0
fi

readonly UPDATER_USER="${RUSTSERVER_UPDATER_SERVICE_USER:-rustserver-updater}"
readonly UPDATER_GROUP="${RUSTSERVER_UPDATER_SERVICE_GROUP:-rustserver-updater}"
readonly APPLICATIONS_TEMPLATE="$STEP_DIR/../../config/applications.ini"

create_updater_user_if_missing() {
  if getent passwd "$UPDATER_USER" >/dev/null; then
    return
  fi

  run_as_root useradd --system --user-group --home-dir /var/lib/rustserver/updater \
    --shell /usr/sbin/nologin "$UPDATER_USER" || \
    bootstrap_die "Could not create service user: $UPDATER_USER"
  bootstrap_info "Created dedicated service user: $UPDATER_USER"
}

ensure_configuration_directory() {
  ensure_updater_configuration_directory
}

create_file_without_overwrite() {
  create_updater_configuration_file_without_overwrite "$@"
}

read_updater_token() {
  local source_file="${RUSTSERVER_UPDATER_GITHUB_TOKEN_FILE:-}"
  local token

  if [[ -n "$source_file" ]]; then
    require_private_token_file "$source_file"
    token="$(cat -- "$source_file")"
  else
    bootstrap_die 'Updater GitHub authentication is required; rerun the Production Installer with UPDATER_GITHUB_TOKEN or UPDATER_GITHUB_TOKEN_FILE for non-interactive use'
  fi

  [[ "$token" =~ ^[A-Za-z0-9_=-]+$ ]] || \
    bootstrap_die "Updater GitHub token contains unsupported characters"
  printf '%s\n' "$token"
}

validate_existing_updater_token() {
  local token
  local owner
  local group
  local mode

  owner="$(run_as_root stat -c '%U' -- "$UPDATER_TOKEN_FILE")" || \
    bootstrap_die 'Existing Updater GitHub token ownership could not be inspected'
  group="$(run_as_root stat -c '%G' -- "$UPDATER_TOKEN_FILE")" || \
    bootstrap_die 'Existing Updater GitHub token group could not be inspected'
  mode="$(run_as_root stat -c '%a' -- "$UPDATER_TOKEN_FILE")" || \
    bootstrap_die 'Existing Updater GitHub token permissions could not be inspected'
  [[ "$owner" == root && "$group" == root && "$mode" == 600 ]] || \
    bootstrap_die 'Existing Updater GitHub token must be owned by root:root with mode 0600'
  token="$(read_root_regular_file "$UPDATER_TOKEN_FILE")"
  [[ "$token" =~ ^[A-Za-z0-9_=-]+$ ]] || \
    bootstrap_die 'Existing Updater GitHub token contains unsupported characters'
}

create_application_registry() {
  if root_path_exists "$UPDATER_REGISTRY_FILE"; then
    require_root_regular_file "$UPDATER_REGISTRY_FILE"
    bootstrap_skip "Preserving existing persistent configuration: $UPDATER_REGISTRY_FILE"
    return
  fi

  require_regular_file "$APPLICATIONS_TEMPLATE"
  run_as_root cat -- "$APPLICATIONS_TEMPLATE" | \
    create_file_without_overwrite "$UPDATER_REGISTRY_FILE" 0640 root "$UPDATER_GROUP"
}

migrate_auth_application_registration() {
  local temporary_file
  local backup_file
  local migration_exit_code

  root_path_exists "$UPDATER_REGISTRY_FILE" || \
    bootstrap_die "Application registry is missing after initialization: $UPDATER_REGISTRY_FILE"
  require_root_regular_file "$UPDATER_REGISTRY_FILE"
  require_regular_file "$APPLICATIONS_TEMPLATE"

  temporary_file="$(run_as_root mktemp --tmpdir="$UPDATER_REGISTRY_DIRECTORY" '.applications.ini.auth-migration.XXXXXX')" || \
    bootstrap_die 'Could not create a temporary Auth registration migration file'

  if run_as_root python3 - "$UPDATER_REGISTRY_FILE" "$APPLICATIONS_TEMPLATE" "$temporary_file" <<'PY'
import re
import sys

registry_path, template_path, output_path = sys.argv[1:]
auth_section = "Application:RustServer.Auth"
auth_name = "RustServer.Auth"
auth_directory = "auth"
auth_service = "rustserver-auth"
required_auth_values = {
    "Repository": "RustServer.Auth",
    "Directory": auth_directory,
    "Service": auth_service,
    "Entrypoint": "RustServer.Auth.dll",
    "HealthPort": "5286",
    "HealthPath": "/health/ready",
}

section_pattern = re.compile(r"^\[([^\[\]]+)\]$")

def fail(message):
    raise SystemExit(f"Cannot safely migrate Auth application registration: {message}")

def parse_sections(content, source):
    sections = {}
    current = None
    for line_number, raw_line in enumerate(content.splitlines(), 1):
        line = raw_line.strip()
        if not line or line.startswith(("#", ";")):
            continue
        section_match = section_pattern.fullmatch(line)
        if section_match:
            current = section_match.group(1)
            if current in sections:
                fail(f"{source} duplicates section '{current}'")
            sections[current] = {}
            continue
        if current is None or "=" not in line:
            fail(f"{source} has invalid syntax at line {line_number}")
        key, value = (part.strip() for part in line.split("=", 1))
        if not key or not value or key in sections[current]:
            fail(f"{source} has an invalid key or value at line {line_number}")
        sections[current][key] = value
    return sections

try:
    with open(registry_path, encoding="utf-8", newline="") as source:
        registry = source.read()
    with open(template_path, encoding="utf-8", newline="") as source:
        template = source.read()
except OSError as error:
    fail(str(error))

registry_sections = parse_sections(registry, "applications.ini")
template_sections = parse_sections(template, "applications template")
if template_sections.get(auth_section) != required_auth_values:
    fail("the bundled Auth registration does not match the production contract")

application_sections = [
    (section, values)
    for section, values in registry_sections.items()
    if section.startswith("Application:")
]
for section, _ in application_sections:
    if section[len("Application:"):].casefold() == auth_name.casefold():
        raise SystemExit(10)

for section, values in application_sections:
    if values.get("Directory") == auth_directory:
        fail(f"directory '{auth_directory}' is already used by '{section}'")
    if values.get("Service", "").casefold() == auth_service.casefold():
        fail(f"service '{auth_service}' is already used by '{section}'")

newline = "\r\n" if "\r\n" in registry else "\n"
suffix = "" if registry.endswith(("\n", "\r")) else newline
if not registry.endswith((newline + newline, "\n\n", "\r\n\r\n")):
    suffix += newline
auth_lines = [f"[{auth_section}]"] + [f"{key}={value}" for key, value in required_auth_values.items()]
candidate = registry + suffix + newline.join(auth_lines) + newline
with open(output_path, "w", encoding="utf-8", newline="") as output:
    output.write(candidate)
PY
  then
    migration_exit_code=0
  else
    migration_exit_code=$?
  fi

  if (( migration_exit_code == 10 )); then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_skip "Preserving existing Auth application registration: $UPDATER_REGISTRY_FILE"
    return
  fi
  if (( migration_exit_code != 0 )); then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Auth application registration migration failed; applications.ini was not changed'
  fi

  run_as_root chown "root:$UPDATER_GROUP" -- "$temporary_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not set ownership on the Auth registration migration file'
  }
  run_as_root chmod 0640 -- "$temporary_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not set permissions on the Auth registration migration file'
  }

  backup_file="$UPDATER_REGISTRY_FILE.before-auth-registration-$(date -u +%Y%m%dT%H%M%SZ)-$$.bak"
  if root_path_exists "$backup_file"; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die "Refusing to overwrite an existing application registry backup: $backup_file"
  fi
  run_as_root cp --preserve=mode,ownership -- "$UPDATER_REGISTRY_FILE" "$backup_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not preserve the application registry before Auth migration'
  }
  if ! run_as_root cmp --silent -- "$UPDATER_REGISTRY_FILE" "$backup_file"; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Application registry changed during Auth migration; it was not replaced'
  fi
  run_as_root mv -Tf -- "$temporary_file" "$UPDATER_REGISTRY_FILE" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not atomically install the Auth application registration'
  }
  bootstrap_info "Added Auth application registration; backup: $backup_file"
}

migrate_plugin_catalog_application_registration() {
  local temporary_file backup_file migration_exit_code
  root_path_exists "$UPDATER_REGISTRY_FILE" || bootstrap_die "Application registry is missing after initialization: $UPDATER_REGISTRY_FILE"
  require_root_regular_file "$UPDATER_REGISTRY_FILE"
  require_regular_file "$APPLICATIONS_TEMPLATE"
  temporary_file="$(run_as_root mktemp --tmpdir="$UPDATER_REGISTRY_DIRECTORY" '.applications.ini.plugin-catalog-migration.XXXXXX')" ||
    bootstrap_die 'Could not create a temporary PluginCatalog registration migration file'

  if run_as_root python3 - "$UPDATER_REGISTRY_FILE" "$APPLICATIONS_TEMPLATE" "$temporary_file" <<'PY'
from configparser import ConfigParser, Error
import sys

registry_path, template_path, output_path = sys.argv[1:]
section = "Application:RustServer.PluginCatalog"
required = {
    "Repository": "RustServer.PluginCatalog",
    "Directory": "plugin-catalog",
    "Service": "rustserver-plugin-catalog",
    "Entrypoint": "RustServer.PluginCatalog.Api.dll",
    "HealthPort": "5116",
    "HealthPath": "/health/ready",
    "InstallHook": "provisioning/provision.sh",
}

def fail(message):
    raise SystemExit(f"Cannot safely migrate PluginCatalog application registration: {message}")

def parse(content, source):
    parser = ConfigParser(interpolation=None, strict=True, empty_lines_in_values=False)
    parser.optionxform = str
    try:
        parser.read_string(content)
    except Error as error:
        fail(f"{source} is malformed: {error}")
    return parser

try:
    with open(registry_path, encoding="utf-8", newline="") as handle:
        registry = handle.read()
    with open(template_path, encoding="utf-8", newline="") as handle:
        template = handle.read()
except OSError as error:
    fail(str(error))

existing = parse(registry, "applications.ini")
reference = parse(template, "applications template")
if not reference.has_section(section) or dict(reference[section]) != required:
    fail("the bundled PluginCatalog registration does not match the production contract")
if existing.has_section(section):
    current = dict(existing[section])
    if current == required:
        raise SystemExit(10)

    legacy = {key: value for key, value in required.items() if key != "InstallHook"}
    if current != legacy:
        fail("the existing PluginCatalog section conflicts with the reviewed registration")

    # Preserve existing bytes (and all other registrations), adding only the
    # reviewed first-install opt-in. The previous file is backed up below.
    newline = "\r\n" if "\r\n" in registry else "\n"
    lines = registry.splitlines(keepends=True)
    heading = f"[{section}]"
    heading_index = next((i for i, line in enumerate(lines) if line.strip() == heading), None)
    if heading_index is None:
        fail("could not locate the existing PluginCatalog section header")
    end_index = next((i for i in range(heading_index + 1, len(lines))
                      if lines[i].strip().startswith("[") and lines[i].strip().endswith("]")), len(lines))
    prefix = "".join(lines[:end_index])
    suffix = "".join(lines[end_index:])
    if prefix and not prefix.endswith(("\r", "\n")):
        prefix += newline
    upgraded = prefix + f"InstallHook={required['InstallHook']}" + newline + suffix
    with open(output_path, "w", encoding="utf-8", newline="") as handle:
        handle.write(upgraded)
    raise SystemExit(0)

for candidate in existing.sections():
    if not candidate.startswith("Application:"):
        continue
    values = existing[candidate]
    if candidate[len("Application:"):].casefold() == "rustserver.plugincatalog":
        fail(f"application name conflicts with '{candidate}'")
    if values.get("Directory") == required["Directory"]:
        fail(f"directory '{required['Directory']}' is already used by '{candidate}'")
    if values.get("Service", "").casefold() == required["Service"].casefold():
        fail(f"service '{required['Service']}' is already used by '{candidate}'")
    if values.get("HealthPort") == required["HealthPort"]:
        fail(f"health port '{required['HealthPort']}' is already used by '{candidate}'")

newline = "\r\n" if "\r\n" in registry else "\n"
suffix = "" if registry.endswith(("\n", "\r")) else newline
if not registry.endswith((newline + newline, "\n\n", "\r\n\r\n")):
    suffix += newline
addition = newline.join([f"[{section}]"] + [f"{key}={value}" for key, value in required.items()]) + newline
with open(output_path, "w", encoding="utf-8", newline="") as handle:
    handle.write(registry + suffix + addition)
PY
  then
    migration_exit_code=0
  else
    migration_exit_code=$?
  fi
  if (( migration_exit_code == 10 )); then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_skip "Preserving existing PluginCatalog application registration: $UPDATER_REGISTRY_FILE"
    return
  fi
  if (( migration_exit_code != 0 )); then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'PluginCatalog application registration migration failed; applications.ini was not changed'
  fi
  run_as_root chown "root:$UPDATER_GROUP" -- "$temporary_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not set PluginCatalog registration ownership'
  }
  run_as_root chmod 0640 -- "$temporary_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not protect PluginCatalog registration'
  }
  backup_file="$UPDATER_REGISTRY_FILE.before-plugin-catalog-registration-$(date -u +%Y%m%dT%H%M%SZ)-$$.bak"
  if root_path_exists "$backup_file"; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die "Refusing to overwrite an existing application registry backup: $backup_file"
  fi
  run_as_root cp --preserve=mode,ownership -- "$UPDATER_REGISTRY_FILE" "$backup_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not preserve the application registry before PluginCatalog migration'
  }
  if ! run_as_root cmp --silent -- "$UPDATER_REGISTRY_FILE" "$backup_file"; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Application registry changed during PluginCatalog migration; it was not replaced'
  fi
  run_as_root mv -Tf -- "$temporary_file" "$UPDATER_REGISTRY_FILE" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not atomically install the PluginCatalog application registration'
  }
  bootstrap_info "Added/upgraded PluginCatalog install registration; backup: $backup_file"
}

create_release_channel() {
  local channel

  if root_path_exists "$UPDATER_RELEASE_CHANNEL_FILE"; then
    read_updater_release_channel >/dev/null
    bootstrap_skip "Preserving existing persistent configuration: $UPDATER_RELEASE_CHANNEL_FILE"
    return
  fi

  channel="$(release_channel_for_initial_installation)"
  create_updater_release_channel "$channel"
}

create_updater_token() {
  if root_path_exists "$UPDATER_TOKEN_FILE"; then
    require_root_regular_file "$UPDATER_TOKEN_FILE"
    validate_existing_updater_token
    bootstrap_skip "Preserving existing Updater GitHub credential"
    return
  fi

  read_updater_token | create_file_without_overwrite "$UPDATER_TOKEN_FILE" 0600 root root
}

main() {
  ensure_sudo_access
  create_updater_user_if_missing
  ensure_configuration_directory
  create_updater_token
  create_application_registry
  migrate_auth_application_registration
  migrate_plugin_catalog_application_registration
  create_release_channel
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
