#!/usr/bin/env bash
# Lightweight Installer tests. They never contact GitHub or run Bootstrap.
set -Eeuo pipefail

TEST_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly TEST_DIR
readonly INSTALLER_SCRIPT="$TEST_DIR/../install.sh"

temporary_directory="$(mktemp -d)"
trap 'rm -rf -- "$temporary_directory"' EXIT

fail() {
  printf 'FAIL: %s\n' "$*" >&2
  exit 1
}

test_default_initial_installation_mode() {
  local output

  output="$(bash -c 'source "$1"; parse_arguments; printf "%s\\n" "$INSTALLER_MODE"' -- "$INSTALLER_SCRIPT")"
  [[ "$output" == 'install' ]] || fail "unexpected default Installer mode: $output"
}

test_unsupported_modes_are_rejected() {
  if bash -c 'source "$1"; parse_arguments upgrade-updater' -- "$INSTALLER_SCRIPT" >/dev/null 2>&1; then
    fail 'unsupported Installer mode was accepted'
  fi
  if bash -c 'source "$1"; parse_arguments upgrade-updater v0.1.0-dev.2' -- "$INSTALLER_SCRIPT" >/dev/null 2>&1; then
    fail 'unsupported Installer mode with a release tag was accepted'
  fi
}

test_failed_bootstrap_download_does_not_run_bootstrap() {
  local workspace="$temporary_directory/failed-download"

  mkdir -p "$workspace"
  if TEST_WORKSPACE="$workspace" bash -c '
    source "$1"
    temporary_directory="$TEST_WORKSPACE"
    INSTALLER_CURL_CONFIG="$temporary_directory/curl.conf"
    : > "$INSTALLER_CURL_CONFIG"
    curl() { return 1; }
    download_bootstrap
  ' -- "$INSTALLER_SCRIPT" >/dev/null 2>&1; then
    fail 'Installer accepted a failed Bootstrap download'
  fi
  [[ ! -e "$workspace/extracted" ]] || \
    fail 'Installer extracted Bootstrap after a failed download'
}

test_masked_input_character_handling() {
  local typed_output
  local pasted_output
  local backspace_output

  typed_output="$(bash -c '
    source "$1"
    MASKED_INPUT_VALUE=""
    for character in t o k e n ""; do
      if masked_input_process_character "$character"; then
        [[ -z "$character" ]] || exit 1
      fi
    done
    [[ "$MASKED_INPUT_VALUE" == token ]]
  ' -- "$INSTALLER_SCRIPT" 2>&1)" || fail 'masked input did not accept normal typing'
  [[ "$typed_output" == '*****' ]] || fail 'normal masked input did not render one asterisk per character'

  pasted_output="$(bash -c '
    source "$1"
    pasted="pasted-token"
    MASKED_INPUT_VALUE=""
    for (( index = 0; index < ${#pasted}; index += 1 )); do
      character="${pasted:index:1}"
      masked_input_process_character "$character" || :
    done
    masked_input_process_character ""
    [[ "$MASKED_INPUT_VALUE" == "$pasted" ]]
  ' -- "$INSTALLER_SCRIPT" 2>&1)" || fail 'masked input did not accept pasted input'
  [[ "$pasted_output" == '************' ]] || fail 'pasted masked input did not render the expected asterisks'

  backspace_output="$(bash -c '
    source "$1"
    MASKED_INPUT_VALUE=""
    for character in a b c $'"'"'\177'"'"' d ""; do
      if masked_input_process_character "$character"; then
        [[ -z "$character" ]] || exit 1
      fi
    done
    [[ "$MASKED_INPUT_VALUE" == abd ]]
  ' -- "$INSTALLER_SCRIPT" 2>&1)" || fail 'masked input did not process Backspace'
  [[ "$backspace_output" == $'***\b \b*' ]] || fail 'Backspace did not erase one displayed asterisk'
}

test_non_interactive_github_token_flows() {
  GITHUB_TOKEN='bootstrap-token' bash -c '
    source "$1"
    obtain_github_token
    [[ "$INSTALLER_GITHUB_TOKEN" == bootstrap-token ]]
  ' -- "$INSTALLER_SCRIPT" || fail 'non-interactive Bootstrap GitHub token was rejected'

  UPDATER_GITHUB_TOKEN='updater-token' bash -c '
    source "$1"
    installer_updater_credential_exists() { return 1; }
    obtain_updater_github_token_if_needed
    [[ "$INSTALLER_UPDATER_GITHUB_TOKEN" == updater-token ]]
  ' -- "$INSTALLER_SCRIPT" || fail 'non-interactive Updater GitHub token was rejected'
}

test_required_plugins_github_token() {
  # A fresh install must not continue without a third dedicated credential.
  if env -u PLUGINS_GITHUB_TOKEN -u PLUGINS_GITHUB_TOKEN_FILE bash -e -c '
    source "$1"
    installer_plugins_credential_exists() { return 1; }
    obtain_plugins_github_token_if_needed
  ' -- "$INSTALLER_SCRIPT" >/dev/null 2>&1; then
    fail 'Installer accepted a missing Plugins GitHub token'
  fi

  PLUGINS_GITHUB_TOKEN='plugins-token' bash -e -c '
    source "$1"
    installer_plugins_credential_exists() { return 1; }
    obtain_plugins_github_token_if_needed
    [[ "$INSTALLER_PLUGINS_GITHUB_TOKEN" == plugins-token ]]
  ' -- "$INSTALLER_SCRIPT" || fail 'a dedicated Plugins token was rejected'

  local token_file="$temporary_directory/plugins-github-token"
  printf 'plugins-token-from-file' > "$token_file"
  chmod 0600 "$token_file"
  PLUGINS_GITHUB_TOKEN_FILE="$token_file" bash -e -c '
    source "$1"
    installer_plugins_credential_exists() { return 1; }
    obtain_plugins_github_token_if_needed
    [[ "$INSTALLER_PLUGINS_GITHUB_TOKEN" == plugins-token-from-file ]]
  ' -- "$INSTALLER_SCRIPT" || fail 'a private Plugins token file was rejected'

  # Repeat install preserves an existing dedicated credential without prompting.
  env -u PLUGINS_GITHUB_TOKEN -u PLUGINS_GITHUB_TOKEN_FILE bash -e -c '
    source "$1"
    installer_plugins_credential_exists() { return 0; }
    obtain_plugins_github_token_if_needed
    [[ -z "${INSTALLER_PLUGINS_GITHUB_TOKEN:-}" ]]
  ' -- "$INSTALLER_SCRIPT" || fail 'Installer rejected an already-provisioned Plugins credential'
}

test_secure_handoff_files_are_private_and_cleaned_up() {
  local workspace="$temporary_directory/secure-workspace"
  local output

  mkdir -p "$workspace"
  if ! output="$(TEST_WORKSPACE="$workspace" bash -c '
    source "$1"
    mktemp() { printf "%s\n" "$TEST_WORKSPACE"; }
    INSTALLER_GITHUB_TOKEN=bootstrap-token
    INSTALLER_UPDATER_GITHUB_TOKEN=updater-token
    INSTALLER_AUTH_GOOGLE_CLIENT_ID=client-id
    INSTALLER_AUTH_GOOGLE_CLIENT_SECRET=client-secret
    INSTALLER_AUTH_ADMIN_USERNAME=admin
    INSTALLER_AUTH_ADMIN_PASSWORD=not-a-real-password
    prepare_secure_workspace
    for file in "$INSTALLER_TOKEN_FILE" "$INSTALLER_UPDATER_TOKEN_FILE" "$INSTALLER_AUTH_PROVISIONING_FILE" "$INSTALLER_CURL_CONFIG"; do
      [[ "$(stat -c %a "$file")" == 600 ]] || exit 1
    done
    grep -Fqx "GOOGLE_CLIENT_ID=client-id" "$INSTALLER_AUTH_PROVISIONING_FILE"
    cleanup
  ' -- "$INSTALLER_SCRIPT" 2>&1)"; then
    fail 'secure credential handoff could not be prepared and cleaned up'
  fi
  [[ ! -e "$workspace" ]] || fail 'secure credential handoff workspace was not cleaned up'
  [[ "$output" != *'bootstrap-token'* && "$output" != *'updater-token'* && "$output" != *'client-secret'* ]] || \
    fail 'a secret appeared in normal Installer output'
}

test_default_initial_installation_mode
test_unsupported_modes_are_rejected
test_failed_bootstrap_download_does_not_run_bootstrap
test_masked_input_character_handling
test_non_interactive_github_token_flows
test_required_plugins_github_token
test_secure_handoff_files_are_private_and_cleaned_up
printf 'OK: Installer tests passed\n'
