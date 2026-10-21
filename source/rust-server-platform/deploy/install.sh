#!/usr/bin/env bash
# Keep this script with LF line endings (.gitattributes enforces this on Windows).
# Downloads and runs the production Bootstrap without cloning this repository.
set -Eeuo pipefail

readonly REPOSITORY="sysdba925/rust-server-platform"
readonly REPOSITORY_BRANCH="develop"
readonly GITHUB_API_BASE_URL="https://api.github.com"

temporary_directory=""
INSTALLER_UPDATER_TOKEN_FILE=""
INSTALLER_PLUGINS_TOKEN_FILE=""
INSTALLER_AUTH_PROVISIONING_FILE=""

installer_die() {
  printf '[RUSTSERVER INSTALLER] FAIL %s\n' "$*" >&2
  exit 1
}

cleanup() {
  local exit_code=$?

  if [[ -n "$temporary_directory" && -d "$temporary_directory" ]]; then
    rm -rf -- "$temporary_directory"
  fi
  exit "$exit_code"
}

usage() {
  cat <<'USAGE'
Usage:
  bash deploy/install.sh [install]

Modes:
  install          Initial installation or infrastructure recovery (default).
USAGE
}

parse_arguments() {
  INSTALLER_MODE="install"

  case "$#" in
    0)
      ;;
    1)
      case "$1" in
        install)
          ;;
        --help|-h)
          usage
          exit 0
          ;;
        *)
          installer_die "Unsupported Installer mode: $1"
          ;;
      esac
      ;;
    *)
      installer_die "Unexpected Installer arguments"
      ;;
  esac
}

require_command() {
  local command_name="$1"

  command -v "$command_name" >/dev/null 2>&1 || \
    installer_die "Required command is unavailable: $command_name"
}

verify_supported_host() {
  [[ -r /etc/os-release ]] || \
    installer_die "Unsupported host: Ubuntu 24.04 x64 is required"

  # shellcheck disable=SC1091
  source /etc/os-release
  [[ "${ID:-}" == "ubuntu" && "${VERSION_ID:-}" == "24.04" ]] || \
    installer_die "Unsupported host: Ubuntu 24.04 is required"
  [[ "$(uname -m)" == "x86_64" ]] || \
    installer_die "Unsupported architecture: x86_64 is required"
}

require_private_token_file() {
  local token_file="$1"
  local mode

  [[ -f "$token_file" && ! -L "$token_file" ]] || \
    installer_die "GitHub token file is missing or unsafe: $token_file"
  mode="$(stat -c '%a' -- "$token_file")"
  (( (8#$mode & 077) == 0 )) || \
    installer_die "GitHub token file must not be readable by group or other users"
}

obtain_github_token() {
  local source_file

  if [[ -n "${GITHUB_TOKEN:-}" ]]; then
    INSTALLER_GITHUB_TOKEN="$GITHUB_TOKEN"
  elif [[ -n "${GITHUB_TOKEN_FILE:-}" ]]; then
    source_file="$GITHUB_TOKEN_FILE"
    require_private_token_file "$source_file"
    INSTALLER_GITHUB_TOKEN="$(<"$source_file")"
  else
    [[ -t 0 ]] || \
      installer_die "GitHub authentication is required; set GITHUB_TOKEN or GITHUB_TOKEN_FILE for non-interactive use"
    read_masked_input 'GitHub bootstrap token: ' INSTALLER_GITHUB_TOKEN
  fi

  [[ "$INSTALLER_GITHUB_TOKEN" =~ ^[A-Za-z0-9_=-]+$ ]] || \
    installer_die "GitHub token contains unsupported characters"
}

read_visible_input() {
  local prompt="$1"
  local variable_name="$2"
  local value

  [[ -t 0 ]] || installer_die 'Interactive input requires a terminal'
  IFS= read -r -p "$prompt" value >&2 || \
    installer_die 'Interactive input was interrupted'
  printf -v "$variable_name" '%s' "$value"
}

# This small unit is separate from terminal reads so its masking behaviour can
# be tested without a pseudo-terminal. It returns success only for Enter.
MASKED_INPUT_VALUE=''

masked_input_process_character() {
  local character="$1"

  case "$character" in
    '')
      printf '\n' >&2
      return 0
      ;;
    $'\177'|$'\b')
      if [[ -n "$MASKED_INPUT_VALUE" ]]; then
        MASKED_INPUT_VALUE="${MASKED_INPUT_VALUE%?}"
        printf '\b \b' >&2
      fi
      return 1
      ;;
    *)
      MASKED_INPUT_VALUE+="$character"
      printf '*' >&2
      return 1
      ;;
  esac
}

# read -s deliberately suppresses terminal echo.  We render only an asterisk
# for each received character so pasted credentials are visibly acknowledged
# without ever rendering their value.
read_masked_input() {
  local prompt="$1"
  local variable_name="$2"
  local character=''

  [[ -t 0 ]] || installer_die 'Interactive input requires a terminal'
  MASKED_INPUT_VALUE=''
  printf '%s' "$prompt" >&2
  while :; do
    character=''
    IFS= read -r -s -n 1 character || {
      printf '\n' >&2
      installer_die 'Interactive input was interrupted'
    }
    if masked_input_process_character "$character"; then
      break
    fi
  done
  printf -v "$variable_name" '%s' "$MASKED_INPUT_VALUE"
}

installer_auth_environment_exists() {
  [[ -e /etc/rustserver/auth.env || -L /etc/rustserver/auth.env ]]
}

installer_updater_credential_exists() {
  [[ -e /etc/rustserver/updater-github-token || -L /etc/rustserver/updater-github-token ]]
}

obtain_updater_github_token_if_needed() {
  local source_file

  installer_updater_credential_exists && return

  if [[ -n "${UPDATER_GITHUB_TOKEN:-}" ]]; then
    INSTALLER_UPDATER_GITHUB_TOKEN="$UPDATER_GITHUB_TOKEN"
  elif [[ -n "${UPDATER_GITHUB_TOKEN_FILE:-}" ]]; then
    source_file="$UPDATER_GITHUB_TOKEN_FILE"
    require_private_token_file "$source_file"
    INSTALLER_UPDATER_GITHUB_TOKEN="$(<"$source_file")"
  else
    [[ -t 0 ]] || \
      installer_die 'Updater GitHub authentication is required; set UPDATER_GITHUB_TOKEN or UPDATER_GITHUB_TOKEN_FILE for non-interactive use'
    read_masked_input 'GitHub updater token: ' INSTALLER_UPDATER_GITHUB_TOKEN
  fi

  [[ "$INSTALLER_UPDATER_GITHUB_TOKEN" =~ ^[A-Za-z0-9_=-]+$ ]] || \
    installer_die 'Updater GitHub token contains unsupported characters'
}

installer_plugins_credential_exists() {
  [[ -e /etc/rustserver/plugins-github-token || -L /etc/rustserver/plugins-github-token ]]
}

obtain_plugins_github_token_if_needed() {
  local source_file
  installer_plugins_credential_exists && return 0

  if [[ -n "${PLUGINS_GITHUB_TOKEN:-}" ]]; then
    INSTALLER_PLUGINS_GITHUB_TOKEN="$PLUGINS_GITHUB_TOKEN"
  elif [[ -n "${PLUGINS_GITHUB_TOKEN_FILE:-}" ]]; then
    source_file="$PLUGINS_GITHUB_TOKEN_FILE"
    require_private_token_file "$source_file"
    INSTALLER_PLUGINS_GITHUB_TOKEN="$(<"$source_file")"
  else
    [[ -t 0 ]] ||
      installer_die 'Plugins GitHub authentication is required; set PLUGINS_GITHUB_TOKEN or PLUGINS_GITHUB_TOKEN_FILE for non-interactive use'
    read_masked_input 'GitHub plugins token: ' INSTALLER_PLUGINS_GITHUB_TOKEN
  fi
  [[ "$INSTALLER_PLUGINS_GITHUB_TOKEN" =~ ^[A-Za-z0-9_=-]+$ ]] ||
    installer_die 'Plugins GitHub token contains unsupported characters'
}

obtain_auth_credentials_if_needed() {
  installer_auth_environment_exists && return

  [[ -t 0 ]] || \
    installer_die 'Auth credentials are required for a new installation; run the Installer interactively'
  read_visible_input 'Google Client ID: ' INSTALLER_AUTH_GOOGLE_CLIENT_ID
  read_masked_input 'Google Client Secret: ' INSTALLER_AUTH_GOOGLE_CLIENT_SECRET
  read_visible_input 'Admin username: ' INSTALLER_AUTH_ADMIN_USERNAME
  read_masked_input 'Admin password: ' INSTALLER_AUTH_ADMIN_PASSWORD

  [[ -n "$INSTALLER_AUTH_GOOGLE_CLIENT_ID" ]] || installer_die 'Google Client ID must not be empty'
  [[ -n "$INSTALLER_AUTH_GOOGLE_CLIENT_SECRET" ]] || installer_die 'Google Client Secret must not be empty'
  [[ -n "$INSTALLER_AUTH_ADMIN_USERNAME" ]] || installer_die 'Admin username must not be empty'
  [[ -n "$INSTALLER_AUTH_ADMIN_PASSWORD" ]] || installer_die 'Admin password must not be empty'
  (( ${#INSTALLER_AUTH_ADMIN_PASSWORD} >= 12 )) || \
    installer_die 'Admin password must contain at least 12 characters'
}

write_private_file() {
  local destination="$1"
  local value="$2"

  printf '%s\n' "$value" > "$destination" || \
    installer_die "Could not create secure temporary file: $destination"
  chmod 0600 -- "$destination" || \
    installer_die "Could not protect secure temporary file: $destination"
}

write_auth_provisioning_handoff() {
  local destination="$1"

  printf '%s\n' \
    "GOOGLE_CLIENT_ID=$INSTALLER_AUTH_GOOGLE_CLIENT_ID" \
    "GOOGLE_CLIENT_SECRET=$INSTALLER_AUTH_GOOGLE_CLIENT_SECRET" \
    "ADMIN_USERNAME=$INSTALLER_AUTH_ADMIN_USERNAME" \
    "ADMIN_PASSWORD=$INSTALLER_AUTH_ADMIN_PASSWORD" > "$destination" || \
    installer_die 'Could not create secure temporary Auth credential handoff'
  chmod 0600 -- "$destination" || \
    installer_die 'Could not protect secure temporary Auth credential handoff'
}

prepare_secure_workspace() {
  umask 077
  temporary_directory="$(mktemp -d)" || installer_die "Could not create temporary Installer directory"
  INSTALLER_TOKEN_FILE="$temporary_directory/github-token"
  INSTALLER_CURL_CONFIG="$temporary_directory/curl.conf"

  write_private_file "$INSTALLER_TOKEN_FILE" "$INSTALLER_GITHUB_TOKEN"
  printf 'header = "Authorization: Bearer %s"\n' "$INSTALLER_GITHUB_TOKEN" > "$INSTALLER_CURL_CONFIG" || \
    installer_die "Could not create temporary GitHub client configuration"
  chmod 0600 -- "$INSTALLER_CURL_CONFIG"
  if [[ -n "${INSTALLER_UPDATER_GITHUB_TOKEN:-}" ]]; then
    INSTALLER_UPDATER_TOKEN_FILE="$temporary_directory/updater-github-token"
    write_private_file "$INSTALLER_UPDATER_TOKEN_FILE" "$INSTALLER_UPDATER_GITHUB_TOKEN"
  fi
  if [[ -n "${INSTALLER_PLUGINS_GITHUB_TOKEN:-}" ]]; then
    INSTALLER_PLUGINS_TOKEN_FILE="$temporary_directory/plugins-github-token"
    write_private_file "$INSTALLER_PLUGINS_TOKEN_FILE" "$INSTALLER_PLUGINS_GITHUB_TOKEN"
  fi
  if [[ -n "${INSTALLER_AUTH_GOOGLE_CLIENT_ID:-}" ]]; then
    INSTALLER_AUTH_PROVISIONING_FILE="$temporary_directory/auth-credentials.env"
    write_auth_provisioning_handoff "$INSTALLER_AUTH_PROVISIONING_FILE"
  fi
  unset INSTALLER_GITHUB_TOKEN GITHUB_TOKEN INSTALLER_UPDATER_GITHUB_TOKEN \
    UPDATER_GITHUB_TOKEN UPDATER_GITHUB_TOKEN_FILE INSTALLER_PLUGINS_GITHUB_TOKEN \
    PLUGINS_GITHUB_TOKEN PLUGINS_GITHUB_TOKEN_FILE INSTALLER_AUTH_GOOGLE_CLIENT_ID \
    INSTALLER_AUTH_GOOGLE_CLIENT_SECRET INSTALLER_AUTH_ADMIN_USERNAME INSTALLER_AUTH_ADMIN_PASSWORD
}

validate_bootstrap_archive() {
  local archive="$1"
  local entry
  local original_entry
  local component
  local file_type
  local -a components=()

  tar -tzf "$archive" >/dev/null || \
    installer_die "Downloaded Bootstrap archive is not a gzip tar archive"

  while IFS= read -r entry; do
    original_entry="$entry"
    [[ -n "$entry" && "$entry" != /* && "$entry" != *//* ]] || \
      installer_die "Bootstrap archive contains an unsafe path"
    while [[ "$entry" == ./* ]]; do
      entry="${entry#./}"
    done
    [[ -z "$entry" ]] && continue
    [[ "$entry" != */ ]] || entry="${entry%/}"
    IFS='/' read -r -a components <<< "$entry"
    for component in "${components[@]}"; do
      [[ -n "$component" && "$component" != '.' && "$component" != '..' ]] || \
        installer_die "Bootstrap archive contains an unsafe path: $original_entry"
    done
  done < <(tar -tzf "$archive")

  while IFS= read -r file_type; do
    case "${file_type:0:1}" in
      -|d)
        ;;
      *)
        installer_die "Bootstrap archive contains an unsupported link or special file"
        ;;
    esac
  done < <(tar -tvzf "$archive")
}

download_bootstrap() {
  local archive="$temporary_directory/bootstrap.tar.gz"
  local extraction_directory="$temporary_directory/extracted"
  local -a bootstrap_scripts=()

  curl --config "$INSTALLER_CURL_CONFIG" \
    --fail --location --silent --show-error --retry 3 --retry-delay 1 \
    --header 'Accept: application/vnd.github+json' \
    --output "$archive" \
    "$GITHUB_API_BASE_URL/repos/$REPOSITORY/tarball/$REPOSITORY_BRANCH" || \
    installer_die "Could not download Bootstrap from the private repository"
  [[ -s "$archive" ]] || installer_die "Downloaded Bootstrap archive is empty"
  validate_bootstrap_archive "$archive"

  extraction_directory="$temporary_directory/extracted"
  mkdir -p -- "$extraction_directory"
  tar --extract --gzip --file "$archive" --directory "$extraction_directory" \
    --no-same-owner --no-same-permissions || \
    installer_die "Could not extract Bootstrap archive"

  mapfile -t bootstrap_scripts < <(find "$extraction_directory" -type f -path '*/deploy/bootstrap/bootstrap.sh' -print)
  (( ${#bootstrap_scripts[@]} == 1 )) || \
    installer_die "Bootstrap archive has an unexpected layout"
  INSTALLER_BOOTSTRAP_DIRECTORY="$(dirname -- "${bootstrap_scripts[0]}")"
  [[ -r "$INSTALLER_BOOTSTRAP_DIRECTORY/bootstrap.sh" && -d "$INSTALLER_BOOTSTRAP_DIRECTORY/steps" ]] || \
    installer_die "Bootstrap archive is incomplete"
}

run_bootstrap() {
  # Installation defaults are read from the downloaded Bootstrap archive.
  # Keep internal lock and step-path controls owned by the Installer.
  unset RUSTSERVER_BOOTSTRAP_LOCK_FILE RUSTSERVER_BOOTSTRAP_STEPS_DIR
  RUSTSERVER_BOOTSTRAP_MODE="$INSTALLER_MODE" \
  RUSTSERVER_GITHUB_TOKEN_FILE="$INSTALLER_TOKEN_FILE" \
  RUSTSERVER_UPDATER_GITHUB_TOKEN_FILE="$INSTALLER_UPDATER_TOKEN_FILE" \
  RUSTSERVER_BOOTSTRAP_PLUGINS_GITHUB_TOKEN_FILE="$INSTALLER_PLUGINS_TOKEN_FILE" \
  RUSTSERVER_AUTH_PROVISIONING_FILE="$INSTALLER_AUTH_PROVISIONING_FILE" \
  RUSTSERVER_UPDATER_RELEASE_CHANNEL="${UPDATER_RELEASE_CHANNEL:-}" \
  bash "$INSTALLER_BOOTSTRAP_DIRECTORY/bootstrap.sh"
}

main() {
  parse_arguments "$@"
  verify_supported_host
  for required_command in bash chmod curl find mktemp stat tar uname; do
    require_command "$required_command"
  done
  obtain_github_token
  obtain_updater_github_token_if_needed
  obtain_plugins_github_token_if_needed
  obtain_auth_credentials_if_needed
  trap cleanup EXIT
  trap 'exit 129' HUP
  trap 'exit 130' INT
  trap 'exit 143' TERM
  prepare_secure_workspace
  download_bootstrap
  run_bootstrap
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
