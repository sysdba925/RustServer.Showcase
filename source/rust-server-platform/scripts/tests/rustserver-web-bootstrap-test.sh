#!/usr/bin/env bash
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly TEST_PLATFORM_DIR="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"
readonly WEB_SERVICE_STEP="$TEST_PLATFORM_DIR/scripts/bootstrap.d/94-install-rustserver-web-service.sh"
readonly TEMPORARY_DIRECTORY="$(mktemp -d)"
readonly CURL_LOG_PATH="$TEMPORARY_DIRECTORY/curl.log"
trap 'rm -rf -- "$TEMPORARY_DIRECTORY"' EXIT

fail() {
  printf 'FAIL: %s\n' "$*" >&2
  exit 1
}

assert_equals() {
  [[ "$1" == "$2" ]] || fail "expected '$1', got '$2'"
}

curl_response_status=""

curl() {
  printf '%s\n' "$*" >> "$CURL_LOG_PATH"
  printf '%s' "$curl_response_status"
}

sleep() {
  :
}

# Source the testable functions without performing Bootstrap installation.
# shellcheck source=../bootstrap.d/94-install-rustserver-web-service.sh
source "$WEB_SERVICE_STEP"

test_dashboard_url_is_derived_from_the_web_origin() {
  assert_equals \
    'http://127.0.0.1:5173/dashboard/' \
    "$(web_dashboard_url 'http://127.0.0.1:5173/')"
  assert_equals \
    'http://127.0.0.1:5173/dashboard/' \
    "$(web_dashboard_url 'http://127.0.0.1:5173')"
}

test_readiness_requires_a_direct_dashboard_success() {
  WEB_DASHBOARD_URL='http://127.0.0.1:5173/dashboard/'
  curl_response_status='302'
  : > "$CURL_LOG_PATH"

  if wait_for_web_readiness; then
    fail 'readiness accepted a dashboard redirect'
  fi

  assert_equals "$WEB_READINESS_ATTEMPTS" "$(wc -l < "$CURL_LOG_PATH")"
  [[ "$(head -n 1 "$CURL_LOG_PATH")" == *"--write-out %{http_code} $WEB_DASHBOARD_URL" ]] || \
    fail 'readiness did not request the dashboard URL and capture its HTTP status'

  curl_response_status='200'
  : > "$CURL_LOG_PATH"

  wait_for_web_readiness || fail 'readiness rejected a direct dashboard success'
  assert_equals '1' "$(wc -l < "$CURL_LOG_PATH")"
}

test_dashboard_url_is_derived_from_the_web_origin
test_readiness_requires_a_direct_dashboard_success
printf 'RustServer.Web Bootstrap tests passed.\n'
