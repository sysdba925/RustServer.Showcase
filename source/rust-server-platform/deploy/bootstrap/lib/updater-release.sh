#!/usr/bin/env bash
# GitHub Release discovery and validation for the Production Updater.

readonly UPDATER_REPOSITORY='sysdba925/RustServer.Updater'
readonly UPDATER_GITHUB_API_BASE_URL='https://api.github.com'
readonly UPDATER_RELEASES_API_URL="$UPDATER_GITHUB_API_BASE_URL/repos/$UPDATER_REPOSITORY/releases"
# Bootstrap runs each numbered step in a fresh shell.  This root-owned,
# volatile hand-off keeps the channel and the exact tag selected by step 45
# together until step 50 has installed that release.  It is deliberately not
# persistent configuration: the channel file remains the operator's durable
# choice and a subsequent clean-install attempt selects a fresh published tag.
readonly UPDATER_INITIAL_RELEASE_SELECTION_DIRECTORY='/run/rustserver'
readonly UPDATER_INITIAL_RELEASE_SELECTION_FILE="$UPDATER_INITIAL_RELEASE_SELECTION_DIRECTORY/updater-initial-release-selection"

github_curl_config=''
UPDATER_INITIAL_RELEASE_CHANNEL=''
UPDATER_INITIAL_RELEASE_TAG=''

read_updater_release_token_file() {
  local token_file="$1"
  local mode

  require_regular_file "$token_file"
  mode="$(stat -c '%a' -- "$token_file")"
  (( (8#$mode & 077) == 0 )) || \
    bootstrap_die "GitHub token file must not be readable by group or other users: $token_file"

  if [[ -r "$token_file" ]]; then
    cat -- "$token_file"
  elif run_as_root test -r "$token_file"; then
    run_as_root cat -- "$token_file"
  else
    bootstrap_die "GitHub token file is not readable: $token_file"
  fi
}

prepare_github_authentication() {
  local token

  if [[ -n "${RUSTSERVER_GITHUB_TOKEN_FILE:-}" ]]; then
    token="$(read_updater_release_token_file "$RUSTSERVER_GITHUB_TOKEN_FILE")"
  elif [[ -n "${GITHUB_TOKEN:-}" ]]; then
    token="$GITHUB_TOKEN"
  elif [[ -n "${GITHUB_TOKEN_FILE:-}" ]]; then
    token="$(read_updater_release_token_file "$GITHUB_TOKEN_FILE")"
  else
    bootstrap_die 'GitHub authentication is required to download the private Updater release. Set GITHUB_TOKEN or GITHUB_TOKEN_FILE for this shell.'
  fi

  # GitHub tokens do not need whitespace or shell syntax. Restricting the
  # value also keeps curl's temporary configuration format unambiguous.
  [[ "$token" =~ ^[A-Za-z0-9_=-]+$ ]] || \
    bootstrap_die 'GitHub token contains unsupported characters'

  umask 077
  github_curl_config="$(mktemp)"
  printf 'header = "Authorization: Bearer %s"\n' "$token" > "$github_curl_config"
  chmod 0600 -- "$github_curl_config"
  unset token GITHUB_TOKEN
}

cleanup_github_authentication() {
  if [[ -n "$github_curl_config" ]]; then
    rm -f -- "$github_curl_config"
    github_curl_config=''
  fi
}

github_api_get() {
  local url="$1"
  local destination="$2"
  local accept="$3"

  [[ -n "$github_curl_config" && -f "$github_curl_config" ]] || \
    bootstrap_die 'GitHub authentication was not prepared'
  curl --config "$github_curl_config" \
    --fail --location --silent --show-error --retry 3 --retry-delay 1 \
    --header "Accept: $accept" \
    --header 'X-GitHub-Api-Version: 2022-11-28' \
    --output "$destination" \
    "$url" || bootstrap_die 'GitHub API download failed'
  [[ -s "$destination" ]] || bootstrap_die 'GitHub API returned an empty download'
}

validate_release_channel() {
  local channel="$1"

  [[ "$channel" == Development || "$channel" == Production ]] || \
    bootstrap_die 'Updater release channel must be Development or Production'
}

release_tag_matches_channel() {
  local release_tag="$1"
  local channel="$2"

  validate_release_channel "$channel"
  case "$channel" in
    Development)
      [[ "$release_tag" =~ ^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)-dev\.(0|[1-9][0-9]*)$ ]]
      ;;
    Production)
      [[ "$release_tag" =~ ^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]
      ;;
  esac
}

ensure_initial_release_selection_directory() {
  if root_path_exists "$UPDATER_INITIAL_RELEASE_SELECTION_DIRECTORY"; then
    if ! run_as_root test -d "$UPDATER_INITIAL_RELEASE_SELECTION_DIRECTORY" || \
      run_as_root test -L "$UPDATER_INITIAL_RELEASE_SELECTION_DIRECTORY"; then
      bootstrap_die "Initial Updater release selection directory is missing or unsafe: $UPDATER_INITIAL_RELEASE_SELECTION_DIRECTORY"
    fi
  else
    run_as_root install -d -m 0755 -o root -g root -- "$UPDATER_INITIAL_RELEASE_SELECTION_DIRECTORY" || \
      bootstrap_die 'Could not create the initial Updater release selection directory'
  fi

  run_as_root chown root:root -- "$UPDATER_INITIAL_RELEASE_SELECTION_DIRECTORY" || \
    bootstrap_die 'Could not protect the initial Updater release selection directory'
  run_as_root chmod 0755 -- "$UPDATER_INITIAL_RELEASE_SELECTION_DIRECTORY" || \
    bootstrap_die 'Could not protect the initial Updater release selection directory'
}

write_initial_updater_release_selection() {
  local channel="$1"
  local release_tag="$2"
  local temporary_file=''

  validate_release_channel "$channel"
  release_tag_matches_channel "$release_tag" "$channel" || \
    bootstrap_die "Initial Updater release tag does not match the selected $channel channel"
  ensure_initial_release_selection_directory

  temporary_file="$(run_as_root mktemp --tmpdir="$UPDATER_INITIAL_RELEASE_SELECTION_DIRECTORY" '.updater-initial-release-selection.tmp.XXXXXX')" || \
    bootstrap_die 'Could not create initial Updater release selection state'
  if ! {
    printf 'release_channel=%s\n' "$channel"
    printf 'release_tag=%s\n' "$release_tag"
  } | run_as_root tee "$temporary_file" >/dev/null; then
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not write initial Updater release selection state'
  fi
  run_as_root chown root:root -- "$temporary_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not protect initial Updater release selection state'
  }
  run_as_root chmod 0600 -- "$temporary_file" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not protect initial Updater release selection state'
  }
  run_as_root mv -Tf -- "$temporary_file" "$UPDATER_INITIAL_RELEASE_SELECTION_FILE" || {
    run_as_root rm -f -- "$temporary_file"
    bootstrap_die 'Could not save initial Updater release selection state'
  }
}

read_initial_updater_release_selection() {
  local -a state_lines=()
  local channel
  local release_tag

  root_path_exists "$UPDATER_INITIAL_RELEASE_SELECTION_FILE" || \
    bootstrap_die 'Initial Updater release selection is missing; rerun Bootstrap install so steps 45 and 50 select the release together'
  require_root_regular_file "$UPDATER_INITIAL_RELEASE_SELECTION_FILE"
  mapfile -t state_lines < <(read_root_regular_file "$UPDATER_INITIAL_RELEASE_SELECTION_FILE")
  (( ${#state_lines[@]} == 2 )) || \
    bootstrap_die 'Initial Updater release selection state has an invalid format'
  [[ "${state_lines[0]}" == release_channel=* && "${state_lines[1]}" == release_tag=* ]] || \
    bootstrap_die 'Initial Updater release selection state has an invalid format'

  channel="${state_lines[0]#release_channel=}"
  release_tag="${state_lines[1]#release_tag=}"
  validate_release_channel "$channel"
  release_tag_matches_channel "$release_tag" "$channel" || \
    bootstrap_die 'Initial Updater release selection state has an invalid release tag'

  UPDATER_INITIAL_RELEASE_CHANNEL="$channel"
  UPDATER_INITIAL_RELEASE_TAG="$release_tag"
}

remove_initial_updater_release_selection() {
  root_path_exists "$UPDATER_INITIAL_RELEASE_SELECTION_FILE" || return 0
  require_root_regular_file "$UPDATER_INITIAL_RELEASE_SELECTION_FILE"
  run_as_root rm -f -- "$UPDATER_INITIAL_RELEASE_SELECTION_FILE" || \
    bootstrap_die 'Could not remove initial Updater release selection state'
}

release_channel_for_initial_installation() {
  local configured_channel="${RUSTSERVER_UPDATER_RELEASE_CHANNEL:-}"

  if [[ -n "$configured_channel" ]]; then
    validate_release_channel "$configured_channel"
    printf '%s\n' "$configured_channel"
    return
  fi

  [[ -t 0 ]] || \
    bootstrap_die 'Updater release channel is required for a non-interactive installation; set UPDATER_RELEASE_CHANNEL to Development or Production'
  read -r -p 'Updater release channel (Development/Production): ' configured_channel >&2
  printf '\n' >&2
  validate_release_channel "$configured_channel"
  printf '%s\n' "$configured_channel"
}

select_latest_updater_release_from_metadata() {
  local releases_metadata="$1"
  local channel="$2"

  validate_release_channel "$channel"
  python3 - "$releases_metadata" "$channel" <<'PY'
import json
import re
import sys

metadata_path, channel = sys.argv[1:]
stable = re.compile(r"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")
development = re.compile(r"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)-dev\.(0|[1-9]\d*)$")

def fail(message):
    raise SystemExit(f"Invalid GitHub Releases metadata: {message}")

try:
    with open(metadata_path, encoding="utf-8") as metadata_file:
        releases = json.load(metadata_file)
except (OSError, json.JSONDecodeError) as error:
    fail(str(error))

if not isinstance(releases, list):
    fail("the releases endpoint must return an array")

candidates = []
for index, release in enumerate(releases):
    if not isinstance(release, dict):
        fail(f"release at index {index} is not an object")
    tag = release.get("tag_name")
    draft = release.get("draft")
    prerelease = release.get("prerelease")
    if not isinstance(tag, str) or not tag:
        fail(f"release at index {index} has an invalid tag_name")
    if type(draft) is not bool:
        fail(f"release {tag!r} has a non-boolean draft value")
    if type(prerelease) is not bool:
        fail(f"release {tag!r} has a non-boolean prerelease value")
    if draft:
        continue
    pattern = development if channel == "Development" else stable
    match = pattern.fullmatch(tag)
    required_prerelease = channel == "Development"
    if match and prerelease is required_prerelease:
        candidates.append((tuple(int(part) for part in match.groups()), tag))

if not candidates:
    raise SystemExit(
        f"No published RustServer.Updater release matches the {channel} channel. "
        "Publish a matching non-draft release or choose that channel explicitly."
    )

print(max(candidates)[1])
PY
}

append_updater_release_metadata_page() {
  local combined_metadata="$1"
  local page_metadata="$2"

  python3 - "$combined_metadata" "$page_metadata" <<'PY'
import json
import os
import sys

combined_path, page_path = sys.argv[1:]
try:
    with open(combined_path, encoding="utf-8") as combined_file:
        combined = json.load(combined_file)
    with open(page_path, encoding="utf-8") as page_file:
        page = json.load(page_file)
except (OSError, json.JSONDecodeError) as error:
    raise SystemExit(f"Invalid GitHub Releases metadata: {error}")

if not isinstance(combined, list) or not isinstance(page, list):
    raise SystemExit("Invalid GitHub Releases metadata: the releases endpoint must return an array")

temporary_path = f"{combined_path}.tmp"
with open(temporary_path, "w", encoding="utf-8") as output:
    json.dump(combined + page, output, separators=(",", ":"))
os.replace(temporary_path, combined_path)
print(len(page))
PY
}

download_updater_releases_metadata() {
  local destination="$1"
  local page=1
  local page_metadata
  local page_count

  printf '[]' > "$destination" || \
    bootstrap_die 'Could not create temporary GitHub Releases metadata'
  while :; do
    (( page <= 1000 )) || \
      bootstrap_die 'GitHub Releases pagination exceeded the supported limit'
    page_metadata="${destination}.page-$page"
    github_api_get "$UPDATER_RELEASES_API_URL?per_page=100&page=$page" \
      "$page_metadata" 'application/vnd.github+json'
    if ! page_count="$(append_updater_release_metadata_page "$destination" "$page_metadata")"; then
      bootstrap_die 'GitHub Releases metadata is invalid'
    fi
    rm -f -- "$page_metadata"
    [[ "$page_count" =~ ^[0-9]+$ ]] || \
      bootstrap_die 'GitHub Releases metadata page count is invalid'
    (( page_count < 100 )) && break
    page=$((page + 1))
  done
}

discover_latest_updater_release() {
  local channel="$1"
  local metadata_file="$2"

  download_updater_releases_metadata "$metadata_file"
  select_latest_updater_release_from_metadata "$metadata_file" "$channel"
}

validate_requested_updater_release_metadata() {
  local release_metadata="$1"
  local requested_tag="$2"
  local release_channel="${3:-}"

  [[ -z "$release_channel" ]] || validate_release_channel "$release_channel"
  python3 - "$release_metadata" "$requested_tag" "$release_channel" <<'PY'
import json
import re
import sys

metadata_path, requested_tag, channel = sys.argv[1:]

def fail(message):
    raise SystemExit(f"Invalid GitHub release metadata: {message}")

try:
    with open(metadata_path, encoding="utf-8") as metadata_file:
        release = json.load(metadata_file)
except (OSError, json.JSONDecodeError) as error:
    fail(str(error))

if not isinstance(release, dict):
    fail("the release endpoint must return an object")
if release.get("tag_name") != requested_tag:
    fail(f"expected tag_name {requested_tag!r}")
if type(release.get("draft")) is not bool or type(release.get("prerelease")) is not bool:
    fail("draft and prerelease must be booleans")
if release["draft"]:
    fail("draft releases cannot be installed")
if not isinstance(release.get("assets"), list):
    fail("assets must be an array")
if channel:
    pattern = (re.compile(r"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)-dev\.(0|[1-9]\d*)$")
               if channel == "Development"
               else re.compile(r"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$"))
    expected_prerelease = channel == "Development"
    if not pattern.fullmatch(requested_tag) or release["prerelease"] is not expected_prerelease:
        fail(f"release does not match the selected {channel} channel")
PY
}

release_asset_api_url() {
  local release_metadata="$1"
  local wanted_asset="$2"

  python3 - "$release_metadata" "$wanted_asset" <<'PY'
import json
import re
import sys

metadata_path, wanted_name = sys.argv[1:]
asset_url = re.compile(
    r"^https://api\.github\.com/repos/sysdba925/RustServer\.Updater/releases/assets/[1-9][0-9]*$"
)
try:
    with open(metadata_path, encoding="utf-8") as metadata_file:
        release = json.load(metadata_file)
except (OSError, json.JSONDecodeError) as error:
    raise SystemExit(f"Invalid GitHub release metadata: {error}")

assets = release.get("assets") if isinstance(release, dict) else None
if not isinstance(assets, list):
    raise SystemExit("Invalid GitHub release metadata: assets must be an array")

matches = []
for index, asset in enumerate(assets):
    if not isinstance(asset, dict):
        raise SystemExit(f"Invalid GitHub release metadata: asset at index {index} is not an object")
    name = asset.get("name")
    url = asset.get("url")
    if not isinstance(name, str) or not isinstance(url, str):
        raise SystemExit(f"Invalid GitHub release metadata: asset at index {index} has an invalid name or URL")
    if name == wanted_name:
        if not asset_url.fullmatch(url):
            raise SystemExit(f"Invalid GitHub release metadata: asset {wanted_name!r} has an unsafe API URL")
        matches.append(url)

if len(matches) != 1:
    raise SystemExit(f"Expected exactly one release asset named {wanted_name!r}, found {len(matches)}")

print(matches[0])
PY
}
