#!/usr/bin/env bash
# Lightweight, dependency-free tests. They never invoke production steps.
set -Eeuo pipefail

TEST_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly TEST_DIR
BOOTSTRAP_DIR="$(cd -- "$TEST_DIR/.." && pwd -P)"
readonly BOOTSTRAP_DIR
readonly BOOTSTRAP_SCRIPT="$BOOTSTRAP_DIR/bootstrap.sh"
readonly COMMON_LIBRARY="$BOOTSTRAP_DIR/lib/common.sh"
readonly UPDATER_RELEASE_LIBRARY="$BOOTSTRAP_DIR/lib/updater-release.sh"
readonly UPDATER_INSTALL_STEP="$BOOTSTRAP_DIR/steps/50-install-updater.sh"
readonly UPDATER_CHANNEL_STEP="$BOOTSTRAP_DIR/steps/45-select-updater-release-channel.sh"
readonly UPDATER_STATE_STEP="$BOOTSTRAP_DIR/steps/55-configure-updater-state.sh"
readonly NGINX_STATICWEB_STEP="$BOOTSTRAP_DIR/steps/75-configure-nginx-staticweb.sh"
readonly NGINX_STATICWEB_CONFIGURATION="$BOOTSTRAP_DIR/../config/rustserver-web.nginx.conf"
readonly LANDING_SOURCE="$BOOTSTRAP_DIR/../config/landing/index.html"
readonly APPLICATIONS_TEMPLATE="$BOOTSTRAP_DIR/../config/applications.ini"
readonly AUTH_PROVISION_STEP="$BOOTSTRAP_DIR/steps/72-provision-auth.sh"
readonly AUTH_SERVICE_ASSET="$BOOTSTRAP_DIR/assets/rustserver-auth.service"
readonly RCON_PROVISION_STEP="$BOOTSTRAP_DIR/steps/73-provision-rcon.sh"
readonly RCON_SERVICE_ASSET="$BOOTSTRAP_DIR/assets/rustserver-rcon-grpc.service"
readonly RCON_SECRET_PERMISSIONS_LIBRARY="$BOOTSTRAP_DIR/lib/rcon-secret-permissions.sh"
readonly PRODUCTION_SERVICES_PROVISIONER="$BOOTSTRAP_DIR/provision-services.sh"
readonly TERMINAL_TOOLS_PRODUCTION_STEP="$BOOTSTRAP_DIR/steps/22-install-terminal-tools.sh"
readonly TERMINAL_TOOLS_REFERENCE="$BOOTSTRAP_DIR/assets/tools"
readonly CONTROL_SERVICE_UNIT="$BOOTSTRAP_DIR/../systemd/rustserver-control.service"

temporary_directory="$(mktemp -d)"
trap 'rm -rf -- "$temporary_directory"' EXIT
readonly BOOTSTRAP_LOCK_FILE="$temporary_directory/bootstrap.lock"
: > "$BOOTSTRAP_LOCK_FILE"
BOOTSTRAP_TESTS_RUN=0

fail() {
  printf 'FAIL: %s\n' "$*" >&2
  exit 1
}

assert_contains() {
  local needle="$1"
  local file="$2"

  grep -Fqx -- "$needle" "$file" >/dev/null || \
    fail "expected '$needle' in $file"
}

assert_not_exists() {
  local path="$1"

  [[ ! -e "$path" && ! -L "$path" ]] || fail "unexpected path exists: $path"
}

assert_equals() {
  [[ "$1" == "$2" ]] || fail "expected '$1', got '$2'"
}

assert_text_has_line() {
  local expected_line="$1"
  local text="$2"

  grep -Fqx -- "$expected_line" <<< "$text" >/dev/null || \
    fail "expected generated text to contain: $expected_line"
}

assert_nginx_location_contains() {
  local location_line="$1"
  local expected_line="$2"
  local configuration_file="$3"

  NGINX_LOCATION_LINE="$location_line" NGINX_EXPECTED_LINE="$expected_line" awk '
    BEGIN {
      location_line = ENVIRON["NGINX_LOCATION_LINE"]
      expected_line = ENVIRON["NGINX_EXPECTED_LINE"]
    }
    $0 == location_line {
      in_location = 1
    }
    in_location {
      if ($0 == expected_line) {
        found = 1
      }

      opens = gsub(/\{/, "{")
      closes = gsub(/\}/, "}")
      depth += opens - closes

      if (depth == 0) {
        exit
      }
    }
    END {
      exit found ? 0 : 1
    }
  ' "$configuration_file" || fail "expected '$expected_line' in Nginx location: $location_line"
}

run_test() {
  local test_name="$1"
  local selected_test="${RUSTSERVER_BOOTSTRAP_TEST:-}"

  if [[ -n "$selected_test" && "$selected_test" != "$test_name" ]]; then
    return
  fi

  "$test_name"
  BOOTSTRAP_TESTS_RUN=$((BOOTSTRAP_TESTS_RUN + 1))
}

create_isolated_updater_state_step() {
  local configuration_directory="$1"
  local fixture_root
  local fixture_directory

  fixture_root="$(mktemp -d "$temporary_directory/updater-state-fixture.XXXXXX")" || \
    fail 'could not create the isolated Updater-state test fixture'
  fixture_directory="$fixture_root/bootstrap"
  mkdir -p "$fixture_directory/lib" "$fixture_directory/steps" "$fixture_root/config"
  cp -- "$COMMON_LIBRARY" "$fixture_directory/lib/common.sh"
  cp -- "$UPDATER_RELEASE_LIBRARY" "$fixture_directory/lib/updater-release.sh"
  cp -- "$UPDATER_STATE_STEP" "$fixture_directory/steps/55-configure-updater-state.sh"
  cp -- "$BOOTSTRAP_DIR/bootstrap.env.example" "$fixture_directory/bootstrap.env.example"
  cp -- "$APPLICATIONS_TEMPLATE" "$fixture_root/config/applications.ini"
  python3 - "$BOOTSTRAP_DIR/lib/updater-configuration.sh" \
    "$fixture_directory/lib/updater-configuration.sh" "$configuration_directory" <<'PY'
import sys

source_path, destination_path, configuration_directory = sys.argv[1:]
with open(source_path, encoding="utf-8") as source:
    payload = source.read()

replacements = {
    "readonly UPDATER_CONFIGURATION_DIRECTORY='/etc/rustserver'":
        f"readonly UPDATER_CONFIGURATION_DIRECTORY='{configuration_directory}'",
}
for source, replacement in replacements.items():
    if source not in payload:
        raise SystemExit(f"fixture source no longer contains expected setting: {source}")
    payload = payload.replace(source, replacement, 1)

with open(destination_path, "w", encoding="utf-8") as destination:
    destination.write(payload)
PY
  printf '%s\n' "$fixture_directory/steps/55-configure-updater-state.sh"
}

create_isolated_bootstrap_directory() {
  local fixture_root
  local fixture_directory

  fixture_root="$(mktemp -d "$temporary_directory/bootstrap-defaults-fixture.XXXXXX")" || \
    fail 'could not create the isolated Bootstrap-defaults test fixture'
  fixture_directory="$fixture_root/bootstrap"
  mkdir -p "$fixture_directory/lib"
  cp -- "$COMMON_LIBRARY" "$fixture_directory/lib/common.sh"
  printf '%s\n' "$fixture_directory"
}

test_orchestration_order_and_logs() {
  local test_root="$temporary_directory/orchestration"
  local steps_directory="$test_root/steps"
  local runner_directory="$test_root/runner"
  local execution_log="$test_root/execution.log"
  local output="$test_root/output.log"

  mkdir -p "$steps_directory" "$runner_directory/lib"
  cp -- "$BOOTSTRAP_SCRIPT" "$runner_directory/bootstrap.sh"
  cp -- "$COMMON_LIBRARY" "$runner_directory/lib/common.sh"
  printf '%s\n' '#!/usr/bin/env bash' 'exit 0' > "$runner_directory/provision-services.sh"
  printf '%s\n' '#!/usr/bin/env bash' "printf '10\\n' >> \"\$TEST_EXECUTION_LOG\"" > "$steps_directory/10-first.sh"
  printf '%s\n' '#!/usr/bin/env bash' "printf '20\\n' >> \"\$TEST_EXECUTION_LOG\"" > "$steps_directory/20-second.sh"

  TEST_EXECUTION_LOG="$execution_log" RUSTSERVER_BOOTSTRAP_STEPS_DIR="$steps_directory" \
    RUSTSERVER_BOOTSTRAP_LOCK_FILE="$BOOTSTRAP_LOCK_FILE" \
    bash "$runner_directory/bootstrap.sh" > "$output"

  assert_contains '10' "$execution_log"
  assert_contains '20' "$execution_log"
  [[ "$(tr '\n' ' ' < "$execution_log")" == '10 20 ' ]] || fail 'steps did not execute in numeric order'
  grep -F '[RUSTSERVER BOOTSTRAP] RUN  10-first' "$output" >/dev/null || fail 'RUN log is missing'
  grep -F '[RUSTSERVER BOOTSTRAP] OK   20-second' "$output" >/dev/null || fail 'OK log is missing'
}

test_orchestration_stops_on_failure() {
  local test_root="$temporary_directory/failure"
  local steps_directory="$test_root/steps"
  local execution_log="$test_root/execution.log"
  local output="$test_root/output.log"
  local exit_code

  mkdir -p "$steps_directory"
  printf '%s\n' '#!/usr/bin/env bash' "printf '10\\n' >> \"\$TEST_EXECUTION_LOG\"" > "$steps_directory/10-first.sh"
  printf '%s\n' '#!/usr/bin/env bash' "printf '20\\n' >> \"\$TEST_EXECUTION_LOG\"" 'exit 23' > "$steps_directory/20-failing.sh"
  printf '%s\n' '#!/usr/bin/env bash' "printf '30\\n' >> \"\$TEST_EXECUTION_LOG\"" > "$steps_directory/30-never-run.sh"

  set +e
  TEST_EXECUTION_LOG="$execution_log" RUSTSERVER_BOOTSTRAP_STEPS_DIR="$steps_directory" \
    RUSTSERVER_BOOTSTRAP_LOCK_FILE="$BOOTSTRAP_LOCK_FILE" \
    bash "$BOOTSTRAP_SCRIPT" > "$output" 2>&1
  exit_code=$?
  set -e

  [[ "$exit_code" == '23' ]] || fail "expected failure exit code 23, got $exit_code"
  [[ "$(tr '\n' ' ' < "$execution_log")" == '10 20 ' ]] || fail 'bootstrap continued after a failed step'
  grep -F '[RUSTSERVER BOOTSTRAP] FAIL 20-failing (exit 23)' "$output" >/dev/null || fail 'FAIL log is missing'
}

test_bootstrap_defaults_are_version_controlled() {
  local defaults_path

  defaults_path="$(bash -c 'source "$1"; printf "%s\\n" "$BOOTSTRAP_DEFAULTS_FILE"' -- "$COMMON_LIBRARY")"
  [[ "$defaults_path" == "$BOOTSTRAP_DIR/bootstrap.env.example" ]] || \
    fail "Bootstrap defaults path is unexpected: $defaults_path"
  [[ -f "$BOOTSTRAP_DIR/bootstrap.env.example" ]] || \
    fail 'Bootstrap configuration template is missing from the Bootstrap files'
}

test_default_install_mode_rejects_unsupported_modes() {
  local mode

  mode="$(bash -c 'source "$1"; bootstrap_mode; validate_bootstrap_invocation' -- "$COMMON_LIBRARY")"
  [[ "$mode" == 'install' ]] || fail "unexpected default Bootstrap mode: $mode"

  if RUSTSERVER_BOOTSTRAP_MODE=upgrade-updater bash -c 'source "$1"; validate_bootstrap_invocation' -- "$COMMON_LIBRARY" >/dev/null 2>&1; then
    fail 'unsupported Bootstrap mode was accepted'
  fi
  if RUSTSERVER_UPDATER_RELEASE_TAG_OVERRIDE=v0.1.0-dev.2 \
    bash -c 'source "$1"; validate_bootstrap_invocation' -- "$COMMON_LIBRARY" >/dev/null 2>&1; then
    fail 'unsupported release override was accepted'
  fi
}

test_release_channel_selection_and_semver_ordering() {
  local metadata_file="$temporary_directory/release-selection.json"
  local development_tag
  local production_tag
  local channel

  printf '%s\n' \
    '[' \
    '  {"tag_name":"v1.9.0","draft":false,"prerelease":false},' \
    '  {"tag_name":"v1.10.0","draft":false,"prerelease":false},' \
    '  {"tag_name":"v8.0.0","draft":true,"prerelease":false},' \
    '  {"tag_name":"v2.0.0-dev.9","draft":false,"prerelease":true},' \
    '  {"tag_name":"v10.0.0-dev.1","draft":false,"prerelease":true},' \
    '  {"tag_name":"v11.0.0-dev.1","draft":false,"prerelease":false},' \
    '  {"tag_name":"v12.0.0","draft":false,"prerelease":true}' \
    ']' > "$metadata_file"

  development_tag="$(bash -c 'source "$1"; source "$2"; select_latest_updater_release_from_metadata "$3" Development' -- \
    "$COMMON_LIBRARY" "$UPDATER_RELEASE_LIBRARY" "$metadata_file")"
  production_tag="$(bash -c 'source "$1"; source "$2"; select_latest_updater_release_from_metadata "$3" Production' -- \
    "$COMMON_LIBRARY" "$UPDATER_RELEASE_LIBRARY" "$metadata_file")"
  channel="$(RUSTSERVER_UPDATER_RELEASE_CHANNEL=Production bash -c 'source "$1"; source "$2"; release_channel_for_initial_installation' -- \
    "$COMMON_LIBRARY" "$UPDATER_RELEASE_LIBRARY")"

  assert_equals 'v10.0.0-dev.1' "$development_tag"
  assert_equals 'v1.10.0' "$production_tag"
  assert_equals 'Production' "$channel"
}

test_no_matching_or_invalid_release_metadata_is_rejected() {
  local no_match_file="$temporary_directory/no-matching-release.json"
  local invalid_file="$temporary_directory/invalid-release.json"
  local output_file="$temporary_directory/release-metadata-error.log"

  printf '%s\n' '[{"tag_name":"v1.0.0","draft":false,"prerelease":false}]' > "$no_match_file"
  if bash -c 'source "$1"; source "$2"; select_latest_updater_release_from_metadata "$3" Development' -- \
    "$COMMON_LIBRARY" "$UPDATER_RELEASE_LIBRARY" "$no_match_file" > "$output_file" 2>&1; then
    fail 'Development selection accepted a Production-only release list'
  fi
  grep -F 'No published RustServer.Updater release matches the Development channel' "$output_file" >/dev/null || \
    fail 'missing-release error is not actionable'

  printf '%s\n' '[{"tag_name":"v1.0.0","draft":"false","prerelease":false}]' > "$invalid_file"
  if bash -c 'source "$1"; source "$2"; select_latest_updater_release_from_metadata "$3" Production' -- \
    "$COMMON_LIBRARY" "$UPDATER_RELEASE_LIBRARY" "$invalid_file" > "$output_file" 2>&1; then
    fail 'invalid GitHub Releases metadata was accepted'
  fi
  grep -F 'Invalid GitHub Releases metadata' "$output_file" >/dev/null || \
    fail 'invalid GitHub Releases metadata did not report a clear error'
}

test_channel_is_resolved_before_updater_installation() {
  local event_log="$temporary_directory/channel-before-install.log"

  CHANNEL_SELECTION_EVENT_LOG="$event_log" bash -c '
    source "$1"
    ensure_sudo_access() { :; }
    create_updater_user_if_missing() { :; }
    ensure_updater_configuration_directory() { :; }
    root_path_exists() { return 1; }
    release_channel_for_initial_installation() { printf "%s\\n" Development; }
    prepare_github_authentication() { :; }
    discover_latest_updater_release() {
      printf "discover:%s\\n" "$1" >> "$CHANNEL_SELECTION_EVENT_LOG"
      printf "%s\\n" v2.0.0-dev.3
    }
    create_updater_release_channel() {
      printf "persist:%s\\n" "$1" >> "$CHANNEL_SELECTION_EVENT_LOG"
    }
    write_initial_updater_release_selection() {
      printf "select:%s:%s\\n" "$1" "$2" >> "$CHANNEL_SELECTION_EVENT_LOG"
    }
    main
  ' -- "$UPDATER_CHANNEL_STEP" >/dev/null

  [[ "$(tr '\n' ' ' < "$event_log")" == 'discover:Development persist:Development select:Development:v2.0.0-dev.3 ' ]] || \
    fail 'release channel was not resolved before the Updater installation could start'
}

test_initial_release_selection_is_transferred_between_processes() {
  local selection_file="$temporary_directory/initial-release-selection"
  local event_log="$temporary_directory/initial-release-selection.log"

  INITIAL_SELECTION_FILE="$selection_file" INITIAL_SELECTION_EVENT_LOG="$event_log" bash -c '
    source "$1"
    ensure_sudo_access() { :; }
    create_updater_user_if_missing() { :; }
    ensure_updater_configuration_directory() { :; }
    root_path_exists() { return 1; }
    release_channel_for_initial_installation() { printf "%s\\n" Development; }
    prepare_github_authentication() { :; }
    discover_latest_updater_release() { printf "%s\\n" v2.5.0-dev.7; }
    create_updater_release_channel() { printf "%s\\n" "$1" > "$INITIAL_SELECTION_FILE.channel"; }
    write_initial_updater_release_selection() {
      printf "%s|%s\\n" "$1" "$2" > "$INITIAL_SELECTION_FILE"
      printf "selected:%s:%s\\n" "$1" "$2" >> "$INITIAL_SELECTION_EVENT_LOG"
    }
    main
  ' -- "$UPDATER_CHANNEL_STEP" >/dev/null

  INITIAL_SELECTION_FILE="$selection_file" INITIAL_SELECTION_EVENT_LOG="$event_log" bash -c '
    source "$1"
    ensure_sudo_access() { :; }
    load_bootstrap_defaults() { :; }
    validate_updater_configuration() { :; }
    require_directory() { :; }
    has_active_updater_release() { return 1; }
    read_updater_release_channel() { cat "$INITIAL_SELECTION_FILE.channel"; }
    read_initial_updater_release_selection() {
      IFS="|" read -r UPDATER_INITIAL_RELEASE_CHANNEL UPDATER_INITIAL_RELEASE_TAG < "$INITIAL_SELECTION_FILE"
      printf "read:%s:%s\\n" "$UPDATER_INITIAL_RELEASE_CHANNEL" "$UPDATER_INITIAL_RELEASE_TAG" >> "$INITIAL_SELECTION_EVENT_LOG"
    }
    set_updater_release_assets() {
      RUSTSERVER_UPDATER_RELEASE_TAG="$1"
      printf "assets:%s\\n" "$1" >> "$INITIAL_SELECTION_EVENT_LOG"
    }
    validate_updater_release_configuration() { :; }
    install_release() { printf "install:%s:%s\\n" "$1" "$RUSTSERVER_UPDATER_RELEASE_TAG" >> "$INITIAL_SELECTION_EVENT_LOG"; }
    activate_existing_release() { printf "activate:%s\\n" "$1" >> "$INITIAL_SELECTION_EVENT_LOG"; }
    remove_initial_updater_release_selection() { rm -f -- "$INITIAL_SELECTION_FILE"; }
    main
  ' -- "$UPDATER_INSTALL_STEP" >/dev/null

  INITIAL_SELECTION_FILE="$selection_file" INITIAL_SELECTION_EVENT_LOG="$event_log" bash -c '
    source "$1"
    root_path_exists() { return 0; }
    read_updater_release_channel() {
      local channel
      channel="$(cat "$INITIAL_SELECTION_FILE.channel")"
      printf "state:%s\\n" "$channel" >> "$INITIAL_SELECTION_EVENT_LOG"
      printf "%s\\n" "$channel"
    }
    release_channel_for_initial_installation() { exit 1; }
    bootstrap_skip() { :; }
    create_release_channel
  ' -- "$UPDATER_STATE_STEP" >/dev/null

  [[ "$(tr '\n' ' ' < "$event_log")" == \
    'selected:Development:v2.5.0-dev.7 read:Development:v2.5.0-dev.7 assets:v2.5.0-dev.7 install:Development:v2.5.0-dev.7 activate:/opt/rustserver/updater/releases/v2.5.0-dev.7 state:Development ' ]] || \
    fail 'release channel and tag were not transferred through steps 45, 50, and 55'
  assert_not_exists "$selection_file"
}

test_bootstrap_lock_rejects_concurrent_operation() {
  local lock_holder_pid

  RUSTSERVER_BOOTSTRAP_LOCK_FILE="$BOOTSTRAP_LOCK_FILE" bash -c '
    source "$1"
    acquire_bootstrap_lock
    sleep 1
  ' -- "$COMMON_LIBRARY" &
  lock_holder_pid=$!
  sleep 0.1

  if RUSTSERVER_BOOTSTRAP_LOCK_FILE="$BOOTSTRAP_LOCK_FILE" \
    bash -c 'source "$1"; acquire_bootstrap_lock' -- "$COMMON_LIBRARY" >/dev/null 2>&1; then
    wait "$lock_holder_pid"
    fail 'Bootstrap lock allowed concurrent operation'
  fi
  wait "$lock_holder_pid"
}

test_staticweb_nginx_contract_is_root_owned_and_release_aware() {
  [[ -x "$NGINX_STATICWEB_STEP" ]] || fail 'Nginx StaticWeb Bootstrap step is not executable'
  [[ -f "$NGINX_STATICWEB_CONFIGURATION" ]] || fail 'Nginx StaticWeb configuration asset is missing'

  grep -Fq 'root /opt/rustserver/web/current;' "$NGINX_STATICWEB_CONFIGURATION" || \
    fail 'Nginx does not serve the StaticWeb current link'
  grep -Fq 'listen 127.0.0.1:8081;' "$NGINX_STATICWEB_CONFIGURATION" || \
    fail 'Nginx readiness listener is not loopback-only'
  grep -Fq 'location = /_rustserver/web-readiness' "$NGINX_STATICWEB_CONFIGURATION" || \
    fail 'Nginx readiness endpoint is missing'
  [[ "$(grep -Fc 'add_header X-RustServer-Release $rustserver_web_release always;' "$NGINX_STATICWEB_CONFIGURATION")" == '1' ]] || \
    fail 'Nginx readiness response must emit exactly one release header'
  grep -Fq 'if (!-f $document_root/index.html)' "$NGINX_STATICWEB_CONFIGURATION" || \
    fail 'Nginx readiness does not verify the selected entry file'
  grep -Fqx '    location ~ ^/(?:login|login\.css|login\.js|admin|portal|signin-google|signin-steam)$ {' "$NGINX_STATICWEB_CONFIGURATION" || \
    fail 'Nginx does not route the reviewed Auth public endpoints'
  [[ "$(grep -Fc '        proxy_pass http://127.0.0.1:5286;' "$NGINX_STATICWEB_CONFIGURATION")" == '2' ]] || \
    fail 'Nginx Auth public routes do not preserve their original URIs'
  assert_nginx_location_contains \
    '    location ^~ /api/auth/ {' \
    '        proxy_pass http://127.0.0.1:5286;' \
    "$NGINX_STATICWEB_CONFIGURATION"
  for auth_location in \
    '    location ~ ^/(?:login|login\.css|login\.js|admin|portal|signin-google|signin-steam)$ {' \
    '    location ^~ /api/auth/ {'; do
    for forwarded_header in \
      '        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;' \
      '        proxy_set_header X-Forwarded-Proto $scheme;' \
      '        proxy_set_header X-Forwarded-Host $host;'; do
      assert_nginx_location_contains \
        "$auth_location" \
        "$forwarded_header" \
        "$NGINX_STATICWEB_CONFIGURATION"
    done
  done
  assert_nginx_location_contains \
    '    location = /_rustserver/auth/authorize-admin {' \
    '        internal;' \
    "$NGINX_STATICWEB_CONFIGURATION"
  assert_nginx_location_contains \
    '    location = /_rustserver/auth/authorize-admin {' \
    '        proxy_pass http://127.0.0.1:5286/api/auth/authorize/admin;' \
    "$NGINX_STATICWEB_CONFIGURATION"

  assert_nginx_location_contains \
    '    location = / {' \
    '        root /opt/rustserver/landing;' \
    "$NGINX_STATICWEB_CONFIGURATION"
  assert_nginx_location_contains \
    '    location = / {' \
    '        try_files /index.html =404;' \
    "$NGINX_STATICWEB_CONFIGURATION"
  for dashboard_location in \
    '    location = /dashboard {' \
    '    location ^~ /dashboard/assets/ {' \
    '    location ^~ /dashboard/ {' \
    '    location @rustserver_dashboard {'; do
    assert_nginx_location_contains "$dashboard_location" \
      '        auth_request /_rustserver/auth/authorize-admin;' "$NGINX_STATICWEB_CONFIGURATION"
    assert_nginx_location_contains "$dashboard_location" \
      '        error_page 401 403 = @rustserver_dashboard_unauthorized;' "$NGINX_STATICWEB_CONFIGURATION"
  done
  [[ "$(grep -Fc 'error_page 401 403 = @rustserver_dashboard_unauthorized;' "$NGINX_STATICWEB_CONFIGURATION")" == 4 ]] || \
    fail 'Authorization redirects must be limited to Dashboard locations'
  assert_nginx_location_contains \
    '    location @rustserver_dashboard_unauthorized {' \
    '        return 302 /;' \
    "$NGINX_STATICWEB_CONFIGURATION"
  assert_nginx_location_contains \
    '    location = /dashboard {' \
    '        auth_request /_rustserver/auth/authorize-admin;' \
    "$NGINX_STATICWEB_CONFIGURATION"
  assert_nginx_location_contains \
    '    location ^~ /dashboard/assets/ {' \
    '        alias /opt/rustserver/web/current/assets/;' \
    "$NGINX_STATICWEB_CONFIGURATION"
  assert_nginx_location_contains \
    '    location ^~ /dashboard/ {' \
    '        try_files $uri $uri/ @rustserver_dashboard;' \
    "$NGINX_STATICWEB_CONFIGURATION"
  assert_nginx_location_contains \
    '    location @rustserver_dashboard {' \
    '        try_files /index.html =404;' \
    "$NGINX_STATICWEB_CONFIGURATION"
  assert_nginx_location_contains \
    '    location = /index.html {' \
    '        return 404;' \
    "$NGINX_STATICWEB_CONFIGURATION"
  assert_nginx_location_contains \
    '    location / {' \
    '        return 404;' \
    "$NGINX_STATICWEB_CONFIGURATION"

  grep -Fqx '    include /etc/rustserver/nginx/applications.d/*.conf;' "$NGINX_STATICWEB_CONFIGURATION" >/dev/null || \
    fail 'Nginx lacks the protected application configuration include point'
  grep -Fq "NGINX_APPLICATION_CONFIGURATION_DIRECTORY='/etc/rustserver/nginx/applications.d'" "$NGINX_STATICWEB_STEP" || \
    fail 'Nginx Bootstrap does not own the application configuration directory'
  grep -Fq 'ensure_root_directory "$NGINX_APPLICATION_CONFIGURATION_DIRECTORY" 0750 root root' "$NGINX_STATICWEB_STEP" || \
    fail 'Nginx application configuration directory is not root-owned and protected'

  for upstream in \
    'proxy_pass http://127.0.0.1:5112/;' \
    'proxy_pass http://127.0.0.1:5113/;' \
    'proxy_pass http://127.0.0.1:5114/;' \
    'proxy_pass http://127.0.0.1:5115/;'; do
    grep -Fq "$upstream" "$NGINX_STATICWEB_CONFIGURATION" || \
      fail "Nginx API proxy is missing: $upstream"
  done

  for api_location in \
    '    location ^~ /control-api/ {' \
    '    location ^~ /versions-api/ {' \
    '    location ^~ /plugins-api/ {' \
    '    location ^~ /snapshots-api/ {'; do
    assert_nginx_location_contains \
      "$api_location" \
      '        auth_request /_rustserver/auth/authorize-admin;' \
      "$NGINX_STATICWEB_CONFIGURATION"
  done
  if grep -Fq 'plugin-catalog-api' "$NGINX_STATICWEB_CONFIGURATION" || \
     grep -Fq '127.0.0.1:5116' "$NGINX_STATICWEB_CONFIGURATION"; then
    fail 'Platform Nginx must not contain a Catalog-specific route or upstream'
  fi

  grep -Fq '[Application:RustServer.Web]' "$APPLICATIONS_TEMPLATE" || \
    fail 'Bootstrap application template does not register RustServer.Web'
  grep -Fq 'Type=StaticWeb' "$APPLICATIONS_TEMPLATE" || \
    fail 'RustServer.Web registration is not StaticWeb'
  grep -Fq 'EntryFile=index.html' "$APPLICATIONS_TEMPLATE" || \
    fail 'RustServer.Web registration does not declare index.html'
  grep -Fq 'HealthPort=8081' "$APPLICATIONS_TEMPLATE" || \
    fail 'RustServer.Web registration does not use the local Nginx readiness port'
  grep -Fq 'HealthPath=/_rustserver/web-readiness' "$APPLICATIONS_TEMPLATE" || \
    fail 'RustServer.Web registration does not use the Nginx readiness route'
}

test_landing_install_preserves_existing_state() {
  local test_dir="$temporary_directory/landing-test"
  mkdir -p "$test_dir"
  bash -c '
    source "$1"
    run_as_root() {
      if [[ "$1" == stat ]]; then
        if [[ "${*: -1}" == */index.html ]]; then printf "%s\n" "${TEST_FILE_MODE:-root:root:644}"; else printf "%s\n" "${TEST_DIR_MODE:-root:root:755}"; fi
      elif [[ "$1" == install ]]; then
        [[ "$*" == *"-m 0644 -o root -g root --"* ]] || exit 1
        cp -- "${@: -2:1}" "${@: -1}"
      else
        "$@"
      fi
    }
    install_landing_page "$2" "$3"
    install_landing_page "$2" "$3"
    if (TEST_FILE_MODE=service:service:666 install_landing_page "$2" "$3") 2>/dev/null; then exit 1; fi
    if (TEST_DIR_MODE=service:service:777 install_landing_page "$2" "$3") 2>/dev/null; then exit 1; fi
    printf "operator content\n" > "$2/index.html"
    if (install_landing_page "$2" "$3") 2>/dev/null; then exit 1; fi
    [[ "$(cat "$2/index.html")" == "operator content" ]]
    rm "$2/index.html"
    if ln -s "$3" "$2/index.html" 2>/dev/null && [[ -L "$2/index.html" ]]; then
      if (install_landing_page "$2" "$3") 2>/dev/null; then exit 1; fi
      [[ -L "$2/index.html" ]]
    else
      printf "SKIP: symbolic links unavailable in this environment\n"
    fi
  ' -- "$NGINX_STATICWEB_STEP" "$test_dir" "$LANDING_SOURCE" || \
    fail 'Landing installation failed to install, repeat safely, or reject conflicts'
  grep -Fq '<a href="/login">LOGIN</a>' "$LANDING_SOURCE" || fail 'Landing login link is missing'
  if grep -Eiq '<script|<link|https?://' "$LANDING_SOURCE"; then
    fail 'Landing page must not depend on scripts or external resources'
  fi
}

test_plugin_catalog_platform_boundary_contract() {
  for registration_line in \
    '[Application:RustServer.PluginCatalog]' \
    'Repository=RustServer.PluginCatalog' \
    'Directory=plugin-catalog' \
    'Service=rustserver-plugin-catalog' \
    'Entrypoint=RustServer.PluginCatalog.Api.dll' \
    'HealthPort=5116' \
    'HealthPath=/health/ready' \
    'InstallHook=provisioning/provision.sh'; do
    grep -Fqx -- "$registration_line" "$APPLICATIONS_TEMPLATE" >/dev/null || \
      fail "PluginCatalog application registration is missing: $registration_line"
  done
  [[ ! -e "$BOOTSTRAP_DIR/../systemd/rustserver-plugin-catalog.service" ]] || \
    fail 'Platform must not ship a Catalog systemd unit'
  [[ ! -e "$BOOTSTRAP_DIR/../plugin-catalog/initial-schema-v1.sql" &&
     ! -e "$BOOTSTRAP_DIR/../plugin-catalog/verify-schema-v1.sql" ]] || \
    fail 'Platform must not ship Catalog SQL artifacts'
  if grep -Eqi 'plugin-catalog|postgresql|createdb|createuser|psql|MediaStorage__|ConnectionStrings__PluginCatalog' "$PRODUCTION_SERVICES_PROVISIONER"; then
    fail 'Production services provisioning retains Catalog-specific dependency knowledge'
  fi
}

test_legacy_catalog_topology_is_preserved_and_ignored() {
  local topology_file="$temporary_directory/legacy-catalog-topology.env"

  cp -- "$BOOTSTRAP_DIR/../../config/topology.env.example" "$topology_file"
  printf '%s\n' \
    'RUSTSERVER_PLUGIN_CATALOG_URL=http://127.0.0.1:5116' \
    'RUSTSERVER_PLUGIN_CATALOG_BIND_HOST=0.0.0.0' >> "$topology_file"

  RUSTSERVER_TOPOLOGY_FILE="$topology_file" bash -c '
    source "$1"
    load_topology_environment
    [[ ! -v RUSTSERVER_PLUGIN_CATALOG_URL ]]
    [[ ! -v RUSTSERVER_PLUGIN_CATALOG_BIND_HOST ]]
  ' -- "$BOOTSTRAP_DIR/../../scripts/lib/topology.sh" || \
    fail 'legacy Catalog topology entries are not safely ignored on rerun'
}

test_auth_registration_migration_and_first_provisioning() {
  local configuration_directory="$temporary_directory/auth-existing-registry"
  local registry_file="$configuration_directory/updater/applications.ini"
  local registry_before="$temporary_directory/auth-existing-registry.before.ini"
  local state_directory="$temporary_directory/auth-state"
  local installation_directory="$temporary_directory/auth-installation"
  local systemd_directory="$temporary_directory/auth-systemd"
  local environment_file="$temporary_directory/auth.env"
  local handoff_file="$temporary_directory/auth-credentials.env"
  local systemctl_log="$temporary_directory/auth-systemctl.log"
  local ownership_log="$temporary_directory/auth-ownership.log"
  local environment_digest
  local state_step

  grep -Fq '[Application:RustServer.Auth]' "$APPLICATIONS_TEMPLATE" || \
    fail 'Bootstrap application template does not register RustServer.Auth'
  for registration_line in \
    'Repository=RustServer.Auth' \
    'Directory=auth' \
    'Service=rustserver-auth' \
    'Entrypoint=RustServer.Auth.dll' \
    'HealthPort=5286' \
    'HealthPath=/health/ready'; do
    grep -Fqx -- "$registration_line" "$APPLICATIONS_TEMPLATE" >/dev/null || \
      fail "RustServer.Auth registration is missing: $registration_line"
  done
  for unit_line in \
    'User=rustserver-auth' \
    'Group=rustserver-auth' \
    'WorkingDirectory=/var/lib/rustserver/auth' \
    'EnvironmentFile=/etc/rustserver/auth.env' \
    'ExecStart=/usr/bin/dotnet /opt/rustserver/auth/current/RustServer.Auth.dll --urls=http://127.0.0.1:5286' \
    'ReadWritePaths=/var/lib/rustserver/auth'; do
    grep -Fqx -- "$unit_line" "$AUTH_SERVICE_ASSET" >/dev/null || \
      fail "Auth systemd unit diverges from the Auth deployment contract: $unit_line"
  done

  mkdir -p "$configuration_directory/updater"
  state_step="$(create_isolated_updater_state_step "$configuration_directory")"
  printf '%s\n' \
    '[Defaults]' \
    'RepositoryOwner=sysdba925' \
    'InstallRoot=/opt/rustserver' \
    'HealthScheme=http' \
    'HealthHost=127.0.0.1' \
    'HealthPath=/health' \
    '' \
    '[Application:RustServer.Control]' \
    'Repository=RustServer.Control' \
    'Directory=control' \
    'Service=rustserver-control' \
    'Entrypoint=RustServer.Control.Api.dll' \
    'HealthPort=5112' > "$registry_file"
  cp -- "$registry_file" "$registry_before"

  RUSTSERVER_BOOTSTRAP_MODE=install \
    RUSTSERVER_UPDATER_SERVICE_USER="$(id -un)" RUSTSERVER_UPDATER_SERVICE_GROUP="$(id -gn)" \
    bash -c '
      source "$1"
      ensure_sudo_access() { :; }
      getent() { return 0; }
      run_as_root() {
        if [[ "$1" == chown ]]; then return 0; fi
        command "$@"
      }
      migrate_auth_application_registration
    ' -- "$state_step"

  python3 - "$registry_before" "$registry_file" <<'PY'
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    before = source.read()
with open(sys.argv[2], encoding="utf-8") as source:
    after = source.read()
if not after.startswith(before):
    raise SystemExit("existing application registrations were changed during Auth migration")
if after.count("[Application:RustServer.Auth]") != 1:
    raise SystemExit("Auth registration was not added exactly once")
PY
  compgen -G "$registry_file.before-auth-registration-*.bak" >/dev/null || \
    fail 'Auth registry migration did not retain the previous registry backup'

  mkdir -p "$systemd_directory"
  printf '%s\n' \
    'GOOGLE_CLIENT_ID=test-client-id' \
    'GOOGLE_CLIENT_SECRET=test-client-secret' \
    'ADMIN_USERNAME=bootstrap-admin' \
    'ADMIN_PASSWORD=not-a-real-password' > "$handoff_file"
  chmod 0600 "$handoff_file"
  RUSTSERVER_BOOTSTRAP_MODE=install \
    RUSTSERVER_AUTH_SERVICE_USER="$(id -un)" RUSTSERVER_AUTH_SERVICE_GROUP="$(id -gn)" \
    RUSTSERVER_AUTH_STATE_DIRECTORY="$state_directory" \
    RUSTSERVER_AUTH_INSTALLATION_DIRECTORY="$installation_directory" \
    RUSTSERVER_AUTH_SYSTEMD_DIRECTORY="$systemd_directory" \
    RUSTSERVER_AUTH_ENVIRONMENT_FILE="$environment_file" \
    RUSTSERVER_AUTH_PROVISIONING_FILE="$handoff_file" \
    AUTH_TEST_SYSTEMCTL_LOG="$systemctl_log" \
    AUTH_TEST_OWNERSHIP_LOG="$ownership_log" \
    bash -c '
      source "$1"
      ensure_sudo_access() { :; }
      getent() { return 0; }
      run_as_root() {
        if [[ "$1" == systemctl ]]; then
          printf "%s\n" "$2" >> "$AUTH_TEST_SYSTEMCTL_LOG"
          return 0
        fi
        if [[ "$1" == stat ]]; then
          case "$3" in
            %U) printf "%s\n" root ;;
            %G) id -gn ;;
            %a) printf "%s\n" 640 ;;
          esac
          return 0
        fi
        if [[ "$1" == chown ]]; then
          printf "%s\n" "$2" >> "$AUTH_TEST_OWNERSHIP_LOG"
          return 0
        fi
        if [[ "$1" == install ]]; then
          shift
          arguments=()
          while (( $# > 0 )); do
            case "$1" in
              -o|-g) shift 2 ;;
              *) arguments+=("$1"); shift ;;
            esac
          done
          command install "${arguments[@]}"
          return
        fi
        command "$@"
      }
      main
    ' -- "$AUTH_PROVISION_STEP"

  [[ -d "$installation_directory/releases" ]] || fail 'Auth releases directory was not provisioned'
  [[ -d "$state_directory" ]] || fail 'Auth persistent state directory was not provisioned'
  [[ -f "$systemd_directory/rustserver-auth.service" ]] || fail 'Auth systemd unit was not provisioned'
  cmp -- "$AUTH_SERVICE_ASSET" "$systemd_directory/rustserver-auth.service" || \
    fail 'provisioned Auth systemd unit differs from the reviewed asset'
  [[ "$(<"$environment_file")" == $'GOOGLE_CLIENT_ID=test-client-id\nGOOGLE_CLIENT_SECRET=test-client-secret\nADMIN_USERNAME=bootstrap-admin\nADMIN_PASSWORD=not-a-real-password' ]] || \
    fail 'Auth environment was not created with the expected canonical content'
  [[ "$(stat -c '%a' "$environment_file")" == '640' ]] || \
    fail 'Auth environment does not have mode 0640'
  grep -Fx "root:$(id -gn)" "$ownership_log" >/dev/null || \
    fail 'Auth environment was not assigned to the Auth service group'
  [[ "$(<"$systemctl_log")" == 'daemon-reload' ]] || \
    fail 'first Auth provisioning enabled, started, stopped, or restarted the service'

  environment_digest="$(sha256sum "$environment_file" | awk '{print $1}')"
  RUSTSERVER_BOOTSTRAP_MODE=install \
    RUSTSERVER_AUTH_SERVICE_USER="$(id -un)" RUSTSERVER_AUTH_SERVICE_GROUP="$(id -gn)" \
    RUSTSERVER_AUTH_STATE_DIRECTORY="$state_directory" \
    RUSTSERVER_AUTH_INSTALLATION_DIRECTORY="$installation_directory" \
    RUSTSERVER_AUTH_SYSTEMD_DIRECTORY="$systemd_directory" \
    RUSTSERVER_AUTH_ENVIRONMENT_FILE="$environment_file" \
    AUTH_TEST_SYSTEMCTL_LOG="$systemctl_log" \
    bash -c '
      source "$1"
      ensure_sudo_access() { :; }
      getent() { return 0; }
      run_as_root() {
        if [[ "$1" == systemctl ]]; then return 0; fi
        if [[ "$1" == stat ]]; then
          case "$3" in
            %U) printf "%s\n" root ;;
            %G) id -gn ;;
            %a) printf "%s\n" 640 ;;
          esac
          return 0
        fi
        [[ "$1" == chown ]] && return 0
        command "$@"
      }
      main
    ' -- "$AUTH_PROVISION_STEP"
  assert_equals "$environment_digest" "$(sha256sum "$environment_file" | awk '{print $1}')"
}

test_plugin_catalog_registration_migration_is_additive_and_safe() {
  local configuration_directory="$temporary_directory/plugin-catalog-registry"
  local registry_file="$configuration_directory/updater/applications.ini"
  local original_file="$temporary_directory/plugin-catalog-registry.original.ini"
  local legacy_file="$temporary_directory/plugin-catalog-registry.legacy.ini"
  local conflict_file="$temporary_directory/plugin-catalog-registry.conflict.ini"
  local state_step
  local digest

  mkdir -p "$configuration_directory/updater"
  state_step="$(create_isolated_updater_state_step "$configuration_directory")"
  printf '%s\n' \
    '[Defaults]' \
    'RepositoryOwner=sysdba925' \
    'InstallRoot=/opt/rustserver' \
    'HealthScheme=http' \
    'HealthHost=127.0.0.1' \
    'HealthPath=/health' \
    '' \
    '[Application:RustServer.Control]' \
    'Repository=RustServer.Control' \
    'Directory=control' \
    'Service=rustserver-control' \
    'Entrypoint=RustServer.Control.Api.dll' \
    'HealthPort=5112' > "$registry_file"
  cp -- "$registry_file" "$original_file"

  RUSTSERVER_UPDATER_SERVICE_USER="$(id -un)" RUSTSERVER_UPDATER_SERVICE_GROUP="$(id -gn)" \
    bash -c '
      source "$1"
      run_as_root() {
        [[ "$1" == chown ]] && return 0
        command "$@"
      }
      migrate_plugin_catalog_application_registration
    ' -- "$state_step"

  python3 - "$original_file" "$registry_file" <<'PY'
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    before = source.read()
with open(sys.argv[2], encoding="utf-8") as source:
    after = source.read()

if not after.startswith(before):
    raise SystemExit("existing application registrations were changed during PluginCatalog migration")
if after.count("[Application:RustServer.PluginCatalog]") != 1:
    raise SystemExit("PluginCatalog registration was not added exactly once")
PY
  for registration_line in \
    'Repository=RustServer.PluginCatalog' \
    'Directory=plugin-catalog' \
    'Service=rustserver-plugin-catalog' \
    'Entrypoint=RustServer.PluginCatalog.Api.dll' \
    'HealthPort=5116' \
    'HealthPath=/health/ready' \
    'InstallHook=provisioning/provision.sh'; do
    grep -Fqx -- "$registration_line" "$registry_file" >/dev/null || \
      fail "PluginCatalog registration is missing: $registration_line"
  done
  compgen -G "$registry_file.before-plugin-catalog-registration-*.bak" >/dev/null || \
    fail 'PluginCatalog registry migration did not retain the previous registry backup'

  digest="$(sha256sum "$registry_file" | awk '{print $1}')"
  RUSTSERVER_UPDATER_SERVICE_USER="$(id -un)" RUSTSERVER_UPDATER_SERVICE_GROUP="$(id -gn)" \
    bash -c '
      source "$1"
      run_as_root() {
        [[ "$1" == chown ]] && return 0
        command "$@"
      }
      migrate_plugin_catalog_application_registration
    ' -- "$state_step"
  [[ "$digest" == "$(sha256sum "$registry_file" | awk '{print $1}')" ]] || \
    fail 'a repeated PluginCatalog registration migration changed the registry'

  # Legacy Catalog registration is upgraded conservatively on an existing VM.
  # Only the single reviewed InstallHook key may be added; state is backed up.
  sed -i '/^InstallHook=provisioning\/provision.sh$/d' "$registry_file"
  cp -- "$registry_file" "$legacy_file"
  RUSTSERVER_UPDATER_SERVICE_USER="$(id -un)" RUSTSERVER_UPDATER_SERVICE_GROUP="$(id -gn)" \
    bash -c '
      source "$1"
      run_as_root() {
        [[ "$1" == chown ]] && return 0
        command "$@"
      }
      migrate_plugin_catalog_application_registration
    ' -- "$state_step"
  grep -Fqx 'InstallHook=provisioning/provision.sh' "$registry_file" || \
    fail 'Existing Catalog registration was not upgraded with reviewed InstallHook'
  python3 - "$legacy_file" "$registry_file" <<'PY'
import sys
before = open(sys.argv[1], encoding="utf-8").read()
after = open(sys.argv[2], encoding="utf-8").read()
if after.replace("InstallHook=provisioning/provision.sh\n", "", 1) != before:
    raise SystemExit("Legacy Catalog registry migration altered unrelated bytes")
PY
  digest="$(sha256sum "$registry_file" | awk '{print $1}')"
  RUSTSERVER_UPDATER_SERVICE_USER="$(id -un)" RUSTSERVER_UPDATER_SERVICE_GROUP="$(id -gn)" \
    bash -c '
      source "$1"
      run_as_root() {
        [[ "$1" == chown ]] && return 0
        command "$@"
      }
      migrate_plugin_catalog_application_registration
    ' -- "$state_step"
  [[ "$digest" == "$(sha256sum "$registry_file" | awk '{print $1}')" ]] || \
    fail 'Repeated Catalog InstallHook migration is not idempotent'

  cp -- "$original_file" "$conflict_file"
  printf '%s\n' '' '[Application:Example]' 'Directory=plugin-catalog' 'Service=example' 'HealthPort=5199' >> "$conflict_file"
  cp -- "$conflict_file" "$registry_file"
  digest="$(sha256sum "$registry_file" | awk '{print $1}')"
  if RUSTSERVER_UPDATER_SERVICE_USER="$(id -un)" RUSTSERVER_UPDATER_SERVICE_GROUP="$(id -gn)" \
    bash -c '
      source "$1"
      run_as_root() {
        [[ "$1" == chown ]] && return 0
        command "$@"
      }
      migrate_plugin_catalog_application_registration
    ' -- "$state_step" >/dev/null 2>&1; then
    fail 'a conflicting PluginCatalog registry migration unexpectedly succeeded'
  fi
  [[ "$digest" == "$(sha256sum "$registry_file" | awk '{print $1}')" ]] || \
    fail 'a rejected PluginCatalog registry migration modified the registry'
}

test_invalid_auth_environment_is_not_overwritten() {
  local environment_file="$temporary_directory/invalid-auth.env"
  local initial_digest
  local output

  printf '%s\n' \
    'GOOGLE_CLIENT_ID=preserve-this-value' \
    'GOOGLE_CLIENT_SECRET=preserve-this-secret' > "$environment_file"
  chmod 0640 "$environment_file"
  initial_digest="$(sha256sum "$environment_file" | awk '{print $1}')"

  if output="$(RUSTSERVER_BOOTSTRAP_MODE=install \
    RUSTSERVER_AUTH_SERVICE_GROUP="$(id -gn)" \
    RUSTSERVER_AUTH_ENVIRONMENT_FILE="$environment_file" \
    RUSTSERVER_AUTH_STATE_DIRECTORY="$temporary_directory/invalid-auth-state" \
    bash -c '
      source "$1"
      run_as_root() {
        if [[ "$1" == stat ]]; then
          case "$3" in
            %U) printf "%s\n" root ;;
            %G) id -gn ;;
            %a) printf "%s\n" 640 ;;
          esac
          return 0
        fi
        command "$@"
      }
      provision_auth_environment
    ' -- "$AUTH_PROVISION_STEP" 2>&1)"; then
    fail 'invalid existing Auth environment was accepted'
  fi

  [[ "$output" == *'Existing Auth environment is invalid'* ]] || \
    fail 'invalid Auth environment did not produce a clear diagnostic'
  assert_equals "$initial_digest" "$(sha256sum "$environment_file" | awk '{print $1}')"
}

test_auth_handoff_requires_google_and_first_admin_values() {
  local missing_google="$temporary_directory/auth-handoff-missing-google.env"
  local missing_admin="$temporary_directory/auth-handoff-missing-admin.env"
  local auth_library="$BOOTSTRAP_DIR/lib/auth-configuration.sh"

  printf '%s\n' \
    'GOOGLE_CLIENT_ID=' \
    'GOOGLE_CLIENT_SECRET=test-client-secret' \
    'ADMIN_USERNAME=bootstrap-admin' \
    'ADMIN_PASSWORD=not-a-real-password' > "$missing_google"
  printf '%s\n' \
    'GOOGLE_CLIENT_ID=test-client-id' \
    'GOOGLE_CLIENT_SECRET=test-client-secret' \
    'ADMIN_USERNAME=bootstrap-admin' \
    'ADMIN_PASSWORD=' > "$missing_admin"
  chmod 0600 "$missing_google" "$missing_admin"

  if bash -c 'source "$1"; read_auth_provisioning_handoff "$2"' -- \
    "$auth_library" "$missing_google" >/dev/null 2>&1; then
    fail 'Auth handoff accepted a missing Google Client ID'
  fi
  if bash -c 'source "$1"; read_auth_provisioning_handoff "$2"' -- \
    "$auth_library" "$missing_admin" >/dev/null 2>&1; then
    fail 'Auth handoff accepted a missing first-admin password'
  fi
}

test_rcon_first_provisioning_preserves_existing_unit() {
  local configuration_directory="$temporary_directory/rcon-configuration"
  local topology_file="$configuration_directory/topology.env"
  local password_file="$configuration_directory/rcon-password"
  local installation_directory="$temporary_directory/rcon-installation"
  local systemd_directory="$temporary_directory/rcon-systemd"
  local systemctl_log="$temporary_directory/rcon-systemctl.log"
  local topology_digest
  local password_digest

  grep -Fq '[Application:RustServer.Rcon]' "$APPLICATIONS_TEMPLATE" || \
    fail 'Bootstrap application template does not register RustServer.Rcon'
  for registration_line in \
    'Repository=RustServer.Rcon' \
    'Directory=rcon' \
    'Service=rustserver-rcon-grpc' \
    'Entrypoint=RustServer.Rcon.Grpc.dll' \
    'HealthPort=50052'; do
    grep -Fqx -- "$registration_line" "$APPLICATIONS_TEMPLATE" >/dev/null || \
      fail "Rcon registration is missing: $registration_line"
  done

  for unit_line in \
    'User=rustserver-rcon' \
    'Group=rustserver-rcon' \
    'WorkingDirectory=/opt/rustserver/rcon/current' \
    'Environment=DOTNET_ENVIRONMENT=Production' \
    'Environment=ASPNETCORE_ENVIRONMENT=Production' \
    'EnvironmentFile=/etc/rustserver/topology.env' \
    'LoadCredential=rcon-password:/etc/rustserver/rcon-password' \
    'InaccessiblePaths=/etc/rustserver/rcon-password' \
    'ExecStart=/bin/bash -ec '\''IFS= read -r RustServerRcon__Password < "$$CREDENTIALS_DIRECTORY/rcon-password" || true; test -n "$$RustServerRcon__Password"; export RustServerRcon__Password; exec /usr/bin/dotnet /opt/rustserver/rcon/current/RustServer.Rcon.Grpc.dll'\'''; do
    grep -Fqx -- "$unit_line" "$RCON_SERVICE_ASSET" >/dev/null || \
      fail "Rcon systemd unit diverges from the Rcon deployment contract: $unit_line"
  done

  mkdir -p "$systemd_directory"
  RUSTSERVER_BOOTSTRAP_MODE=install \
    RUSTSERVER_RCON_SERVICE_USER="$(id -un)" RUSTSERVER_RCON_SERVICE_GROUP="$(id -gn)" \
    RUSTSERVER_RCON_CONFIGURATION_DIRECTORY="$configuration_directory" \
    RUSTSERVER_RCON_INSTALLATION_DIRECTORY="$installation_directory" \
    RUSTSERVER_RCON_SYSTEMD_DIRECTORY="$systemd_directory" \
    RCON_TEST_SYSTEMCTL_LOG="$systemctl_log" \
    bash -c '
      source "$1"
      ensure_sudo_access() { :; }
      getent() { return 0; }
      run_as_root() {
        if [[ "$1" == systemctl ]]; then
          printf "%s\n" "$2" >> "$RCON_TEST_SYSTEMCTL_LOG"
          return 0
        fi
        if [[ "$1" == stat && "$2" == -c && "$3" == "%U:%G:%a" ]]; then
          printf "%s\n" "root:root:600"
          return 0
        fi
        if [[ "$1" == install ]]; then
          shift
          arguments=()
          while (( $# > 0 )); do
            case "$1" in
              -o|-g) shift 2 ;;
              *) arguments+=("$1"); shift ;;
            esac
          done
          command install "${arguments[@]}"
          return
        fi
        command "$@"
      }
      main
    ' -- "$RCON_PROVISION_STEP"

  [[ -d "$installation_directory/releases" ]] || fail 'Rcon releases directory was not provisioned'
  [[ -f "$topology_file" ]] || fail 'Rcon topology configuration was not provisioned'
  for service in CONTROL VERSIONS PLUGINS SNAPSHOTS WEB; do
    assert_contains "RUSTSERVER_${service}_BIND_HOST=127.0.0.1" "$topology_file"
  done
  assert_contains 'RUSTSERVER_RCON_GRPC_URL=http://127.0.0.1:50051' "$topology_file"
  assert_contains 'RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL=http://127.0.0.1:50052' "$topology_file"
  assert_contains 'RUSTSERVER_UPDATER_BIND_ADDRESS=127.0.0.1' "$BOOTSTRAP_DIR/bootstrap.env.example"
  [[ -f "$password_file" ]] || fail 'Rcon password file was not provisioned'
  [[ "$(stat -c '%a' "$topology_file")" == '644' ]] || \
    fail 'Rcon topology configuration does not have mode 0644'
  [[ "$(stat -c '%a' "$password_file")" == '600' ]] || \
    fail 'Rcon password file does not have mode 0600'
  [[ "$(awk 'END { print NR }' "$password_file")" == '1' && -s "$password_file" ]] || \
    fail 'Rcon password file is not a single non-empty line'
  LC_ALL=C grep -Eq '^[0-9a-f]{64}$' "$password_file" || \
    fail 'Generated Rcon password must be a URL-safe 256-bit hexadecimal secret'
  topology_digest="$(sha256sum "$topology_file" | awk '{print $1}')"
  password_digest="$(sha256sum "$password_file" | awk '{print $1}')"
  [[ -f "$systemd_directory/rustserver-rcon-grpc.service" ]] || \
    fail 'Rcon systemd unit was not provisioned'
  cmp -- "$RCON_SERVICE_ASSET" "$systemd_directory/rustserver-rcon-grpc.service" || \
    fail 'provisioned Rcon systemd unit differs from the reviewed asset'
  [[ "$(<"$systemctl_log")" == 'daemon-reload' ]] || \
    fail 'first Rcon provisioning enabled, started, stopped, or restarted the service'

  printf '%s\n' 'operator-managed-rcon-unit' > "$systemd_directory/rustserver-rcon-grpc.service"
  : > "$systemctl_log"
  RUSTSERVER_BOOTSTRAP_MODE=install \
    RUSTSERVER_RCON_SERVICE_USER="$(id -un)" RUSTSERVER_RCON_SERVICE_GROUP="$(id -gn)" \
    RUSTSERVER_RCON_CONFIGURATION_DIRECTORY="$configuration_directory" \
    RUSTSERVER_RCON_INSTALLATION_DIRECTORY="$installation_directory" \
    RUSTSERVER_RCON_SYSTEMD_DIRECTORY="$systemd_directory" \
    RCON_TEST_SYSTEMCTL_LOG="$systemctl_log" \
    bash -c '
      source "$1"
      ensure_sudo_access() { :; }
      getent() { return 0; }
      run_as_root() {
        if [[ "$1" == systemctl ]]; then
          printf "%s\n" "$2" >> "$RCON_TEST_SYSTEMCTL_LOG"
          return 0
        fi
        if [[ "$1" == stat && "$2" == -c && "$3" == "%U:%G:%a" ]]; then
          printf "%s\n" "root:root:600"
          return 0
        fi
        command "$@"
      }
      main
    ' -- "$RCON_PROVISION_STEP"
  assert_equals "$topology_digest" "$(sha256sum "$topology_file" | awk '{print $1}')"
  assert_equals "$password_digest" "$(sha256sum "$password_file" | awk '{print $1}')"
  [[ "$(<"$systemd_directory/rustserver-rcon-grpc.service")" == 'operator-managed-rcon-unit' ]] || \
    fail 'existing Rcon systemd unit was overwritten'
  [[ "$(<"$systemctl_log")" == 'daemon-reload' ]] || \
    fail 'repeat Rcon provisioning enabled, started, stopped, or restarted the service'
}


test_rcon_docker_secret_acl_is_exact_and_idempotent() {
  local secret_file="$temporary_directory/test-rcon-secret"
  printf 'test-value\n' > "$secret_file"
  chmod 0600 "$secret_file"

  bash -c '
    source "$1"
    require_root_regular_file() { [[ -f "$1" ]]; }
    bootstrap_die() { printf "FAIL: %s\n" "$*" >&2; exit 1; }
    fake_mode=600
    run_as_root() {
      case "$1" in
        stat) printf "root:root:%s\n" "$fake_mode" ;;
        getfacl)
          if [[ "$fake_mode" == 600 ]]; then
            printf "%s\n" "$RCON_SECRET_ROOT_ACL"
          else
            printf "%s\n" "$RCON_SECRET_GAME_ACL"
          fi
          ;;
        setfacl) fake_mode=640 ;;
        *) command "$@" ;;
      esac
    }

    rcon_secret_permissions_are_valid "$2"
    rcon_secret_grant_game_container_read "$2"
    [[ "$fake_mode" == 640 ]] || exit 1
    rcon_secret_permissions_are_valid "$2"
    rcon_secret_grant_game_container_read "$2"
  ' -- "$RCON_SECRET_PERMISSIONS_LIBRARY" "$secret_file" || \
    fail 'RCON secret game ACL cannot be provisioned idempotently'

  if bash -c '
    source "$1"
    run_as_root() {
      case "$1" in
        stat) printf "root:root:640\n" ;;
        getfacl) printf "user::rw-\ngroup::r--\nother::---\n" ;;
        *) command "$@" ;;
      esac
    }
    rcon_secret_permissions_are_valid "$2"
  ' -- "$RCON_SECRET_PERMISSIONS_LIBRARY" "$secret_file"; then
    fail 'An overly broad RCON password ACL was accepted'
  fi
}

test_persistent_updater_state_does_not_overwrite_existing_files() {
  local configuration_directory="$temporary_directory/updater-state"
  local token_file="$configuration_directory/github-token"
  local channel_file="$configuration_directory/updater-release-channel"
  local registry_file="$configuration_directory/updater/applications.ini"
  local updater_environment_file="$temporary_directory/updater-state.env"
  local initial_digest
  local final_digest
  local state_step

  mkdir -p "$configuration_directory/updater"
  state_step="$(create_isolated_updater_state_step "$configuration_directory")"
  printf 'first-token\n' | \
    bash -c '
      source "$1"
      run_as_root() { command "$@"; }
      create_file_without_overwrite "$2" 0600 "$(id -un)" "$(id -gn)"
    ' -- "$state_step" "$token_file"
  initial_digest="$(sha256sum "$token_file" | awk '{print $1}')"

  printf 'second-token\n' | \
    bash -c '
      source "$1"
      run_as_root() { command "$@"; }
      create_file_without_overwrite "$2" 0600 "$(id -un)" "$(id -gn)"
    ' -- "$state_step" "$token_file"
  final_digest="$(sha256sum "$token_file" | awk '{print $1}')"

  [[ "$initial_digest" == "$final_digest" ]] || \
    fail 'persistent Updater credential was overwritten on a repeated installation'
  [[ "$(stat -c '%a' "$token_file")" == '600' ]] || \
    fail 'persistent Updater credential does not have mode 0600'

  printf 'Production\n' > "$channel_file"
  printf '%s\n' \
    'RUSTSERVER_UPDATER_BIND_ADDRESS=127.0.0.1' \
    'RUSTSERVER_UPDATER_PORT=8080' > "$updater_environment_file"
  bash -c '
      source "$1"
      run_as_root() { command "$@"; }
      create_release_channel
    ' -- "$state_step"
  [[ "$(<"$channel_file")" == 'Production' ]] || \
    fail 'persistent release channel was overwritten on a repeated installation'

  RUSTSERVER_UPDATER_ENVIRONMENT_FILE="$updater_environment_file" \
    RUSTSERVER_UPDATER_SERVICE_USER="$(id -un)" RUSTSERVER_UPDATER_SERVICE_GROUP="$(id -gn)" \
    bash -c '
      source "$1"
      run_as_root() {
        [[ "$1" == chown ]] && return 0
        command "$@"
      }
      create_application_registry
    ' -- "$state_step"
  [[ -f "$registry_file" ]] || fail 'initial applications registry was not created'
  grep -F 'Entrypoint=RustServer.Updater.Api.dll' "$registry_file" >/dev/null || \
    fail 'initial applications registry has an unexpected format'
}

test_clean_updater_state_initialization() {
  local configuration_directory="$temporary_directory/clean-updater-state"
  local token_source="$temporary_directory/clean-updater-token"
  local state_step

  mkdir -p "$configuration_directory/updater"
  printf 'clean-install-token\n' > "$token_source"
  chmod 0600 "$token_source"
  state_step="$(create_isolated_updater_state_step "$configuration_directory")"

  RUSTSERVER_BOOTSTRAP_MODE=install \
    RUSTSERVER_UPDATER_GITHUB_TOKEN_FILE="$token_source" \
    RUSTSERVER_UPDATER_SERVICE_USER="$(id -un)" RUSTSERVER_UPDATER_SERVICE_GROUP="$(id -gn)" \
    bash -c '
      source "$1"
      ensure_sudo_access() { :; }
      create_updater_user_if_missing() { :; }
      release_channel_for_initial_installation() { printf "%s\\n" Production; }
      run_as_root() {
        [[ "$1" == chown ]] && return 0
        command "$@"
      }
      main
    ' -- "$state_step"

  [[ "$(<"$configuration_directory/updater-github-token")" == 'clean-install-token' ]] || \
    fail 'clean installation did not create the canonical Updater token'
  [[ "$(<"$configuration_directory/updater-release-channel")" == 'Production' ]] || \
    fail 'clean installation did not create the canonical Updater release channel'
  [[ -f "$configuration_directory/updater/applications.ini" ]] || \
    fail 'clean installation did not create the canonical Updater application registry'
}

test_configuration_is_data_not_shell_code() {
  local bootstrap_directory
  local common_library
  local environment_file
  local marker="$temporary_directory/should-not-exist"

  bootstrap_directory="$(create_isolated_bootstrap_directory)"
  common_library="$bootstrap_directory/lib/common.sh"
  environment_file="$bootstrap_directory/bootstrap.env.example"
  printf '%s\n' \
    'RUSTSERVER_UPDATER_BIND_ADDRESS=127.0.0.1' \
    'RUSTSERVER_UPDATER_PORT=9090' > "$environment_file"

  bash -c '
    source "$1"
    load_bootstrap_defaults
    validate_updater_configuration
    [[ "$RUSTSERVER_UPDATER_BIND_ADDRESS" == "127.0.0.1" ]]
    [[ "$RUSTSERVER_UPDATER_PORT" == "9090" ]]
  ' -- "$common_library" || fail 'valid configuration was not loaded'

  printf '%s%s\n' 'RUSTSERVER_UPDATER_PORT=$' "(touch $marker)" > "$environment_file"
  if bash -c 'source "$1"; load_bootstrap_defaults; validate_updater_configuration' -- "$common_library" >/dev/null 2>&1; then
    fail 'shell-looking configuration value was accepted'
  fi
  assert_not_exists "$marker"
}

test_runtime_environment_value_lookup() {
  local environment_file="$temporary_directory/updater.env"
  local bind_address
  local port

  printf '%s\n' \
    'RUSTSERVER_UPDATER_BIND_ADDRESS=127.0.0.1' \
    'RUSTSERVER_UPDATER_PORT=9090' > "$environment_file"

  bind_address="$(bash -c 'source "$1"; environment_value "$2" RUSTSERVER_UPDATER_BIND_ADDRESS' -- "$COMMON_LIBRARY" "$environment_file")"
  port="$(bash -c 'source "$1"; environment_value "$2" RUSTSERVER_UPDATER_PORT' -- "$COMMON_LIBRARY" "$environment_file")"
  [[ "$bind_address" == '127.0.0.1' ]] || fail 'runtime bind address lookup returned the wrong value'
  [[ "$port" == '9090' ]] || fail 'runtime port lookup returned the wrong value'
}

test_published_bundle_layout() {
  local bundle_staging_directory="$temporary_directory/bundle-layout"
  local legacy_staging_directory="$temporary_directory/legacy-layout"
  local selected_directory
  local unit_name

  mkdir -p "$bundle_staging_directory/release/api" \
    "$bundle_staging_directory/release/executor" \
    "$bundle_staging_directory/release/systemd"
  : > "$bundle_staging_directory/release/api/RustServer.Updater.Api.dll"
  : > "$bundle_staging_directory/release/executor/RustServer.Updater.Executor.dll"
  printf '%s\n' \
    '{"formatVersion":1,"releaseVersion":"v0.1.0-dev.2","updaterEntrypoint":"api/RustServer.Updater.Api.dll","executorEntrypoint":"executor/RustServer.Updater.Executor.dll"}' \
    > "$bundle_staging_directory/release/rustserver-updater.bundle.json"
  for unit_name in rustserver-updater.service rustserver-updater-executor.socket \
    rustserver-updater-executor@.service rustserver-updater-recovery.service; do
    : > "$bundle_staging_directory/release/systemd/$unit_name"
  done

  selected_directory="$(bash -c 'source "$1"; release_directory_source_from_staging "$2"' -- "$UPDATER_INSTALL_STEP" "$bundle_staging_directory")"
  [[ "$selected_directory" == "$bundle_staging_directory/release" ]] || \
    fail 'published Updater bundle layout was not accepted'
  if ! bash -c 'source "$1"; require_updater_bundle_release_directory "$2" v0.1.0-dev.2' -- \
    "$UPDATER_INSTALL_STEP" "$selected_directory"; then
    fail 'published Updater bundle did not validate'
  fi

  mkdir -p "$legacy_staging_directory/release"
  : > "$legacy_staging_directory/release/RustServer.Updater.dll"
  if bash -c 'source "$1"; release_directory_source_from_staging "$2"' -- "$UPDATER_INSTALL_STEP" "$legacy_staging_directory" >/dev/null 2>&1; then
    fail 'legacy RustServer.Updater.dll layout was accepted'
  fi
}

test_failed_checksum_is_rejected_before_switching() {
  local archive="$temporary_directory/checksum-archive.tar.gz"
  local checksum_file="$temporary_directory/checksum-archive.tar.gz.sha256"

  printf 'not an archive, but enough to hash\n' > "$archive"
  printf '%064d  RustServer.Updater-v0.1.0-dev.2-linux-x64.tar.gz\n' 0 > "$checksum_file"

  if RUSTSERVER_UPDATER_ASSET=RustServer.Updater-v0.1.0-dev.2-linux-x64.tar.gz \
    bash -c 'source "$1"; verify_archive_checksum "$2" "$3"' -- \
      "$UPDATER_INSTALL_STEP" "$archive" "$checksum_file" >/dev/null 2>&1; then
    fail 'a bad release checksum was accepted'
  fi
}

test_failed_updater_download_is_rejected_before_switching() {
  if bash -c '
    source "$1"
    github_curl_config=/dev/null
    curl() { return 1; }
    github_api_get https://example.invalid/release "$2" application/octet-stream
  ' -- "$UPDATER_INSTALL_STEP" "$temporary_directory/failed-download" >/dev/null 2>&1; then
    fail 'a failed Updater download was accepted'
  fi
}

test_repeated_install_preserves_the_active_updater_release() {
  local output

  output="$(bash -c '
    source "$1"
    ensure_sudo_access() { :; }
    require_directory() { :; }
    has_active_updater_release() { return 0; }
    updater_current_directory() { printf "%s\\n" /opt/rustserver/updater/releases/v9.9.9; }
    discover_latest_updater_release() { exit 91; }
    main
  ' -- "$UPDATER_INSTALL_STEP")"

  [[ "$output" == *'Active Updater release is preserved: v9.9.9'* ]] || \
    fail 'a repeated install did not preserve the active Updater release'
}

test_protected_environment_supports_safe_reruns() {
  local environment_file="$temporary_directory/protected-updater.env"
  local privileged_read_log="$temporary_directory/protected-environment-reads.log"
  local first_bind_address
  local second_bind_address
  local first_digest
  local second_digest

  printf '%s\n' \
    'RUSTSERVER_UPDATER_BIND_ADDRESS=127.0.0.1' \
    'RUSTSERVER_UPDATER_PORT=9090' > "$environment_file"
  first_digest="$(sha256sum "$environment_file" | awk '{print $1}')"
  chmod 000 "$environment_file"
  [[ ! -r "$environment_file" ]] || fail 'protected environment file is unexpectedly readable by the Bootstrap user'

  first_bind_address="$(PROTECTED_ENVIRONMENT_READ_LOG="$privileged_read_log" bash -c '
    source "$1"
    run_as_root() {
      [[ "$1" == "cat" ]] || return 1
      printf "privileged-read\\n" >> "$PROTECTED_ENVIRONMENT_READ_LOG"
      printf "%s\\n" "RUSTSERVER_UPDATER_BIND_ADDRESS=127.0.0.1" "RUSTSERVER_UPDATER_PORT=9090"
    }
    environment_value "$2" RUSTSERVER_UPDATER_BIND_ADDRESS
  ' -- "$COMMON_LIBRARY" "$environment_file")"
  second_bind_address="$(PROTECTED_ENVIRONMENT_READ_LOG="$privileged_read_log" bash -c '
    source "$1"
    run_as_root() {
      [[ "$1" == "cat" ]] || return 1
      printf "privileged-read\\n" >> "$PROTECTED_ENVIRONMENT_READ_LOG"
      printf "%s\\n" "RUSTSERVER_UPDATER_BIND_ADDRESS=127.0.0.1" "RUSTSERVER_UPDATER_PORT=9090"
    }
    environment_value "$2" RUSTSERVER_UPDATER_BIND_ADDRESS
  ' -- "$COMMON_LIBRARY" "$environment_file")"
  chmod 0600 "$environment_file"
  second_digest="$(sha256sum "$environment_file" | awk '{print $1}')"

  [[ "$first_bind_address" == '127.0.0.1' && "$second_bind_address" == '127.0.0.1' ]] || \
    fail 'Bootstrap could not verify a root-protected environment file'
  [[ "$(wc -l < "$privileged_read_log")" == '2' ]] || \
    fail 'Bootstrap did not use the privileged path for protected environment reads'
  [[ "$first_digest" == "$second_digest" ]] || \
    fail 'environment verification modified the protected configuration on rerun'
}

test_protected_bootstrap_defaults_are_rejected_without_modification() {
  local bootstrap_directory
  local common_library
  local environment_file
  local output
  local first_digest
  local second_digest

  bootstrap_directory="$(create_isolated_bootstrap_directory)"
  common_library="$bootstrap_directory/lib/common.sh"
  environment_file="$bootstrap_directory/bootstrap.env.example"
  printf '%s\n' \
    'RUSTSERVER_UPDATER_BIND_ADDRESS=127.0.0.1' \
    'RUSTSERVER_UPDATER_PORT=9090' > "$environment_file"
  first_digest="$(sha256sum "$environment_file" | awk '{print $1}')"
  chmod 000 "$environment_file"
  [[ ! -r "$environment_file" ]] || fail 'protected Bootstrap configuration is unexpectedly readable by the Bootstrap user'

  if output="$(bash -c 'source "$1"; load_bootstrap_defaults; validate_updater_configuration' -- "$common_library" 2>&1)"; then
    chmod 0600 "$environment_file"
    fail 'Bootstrap accepted unreadable archive defaults'
  fi
  chmod 0600 "$environment_file"
  second_digest="$(sha256sum "$environment_file" | awk '{print $1}')"

  [[ "$output" == *'Permission denied'* ]] || \
    fail 'unreadable Bootstrap defaults did not report a readable-file error'
  [[ "$first_digest" == "$second_digest" ]] || \
    fail 'rejected Bootstrap defaults were modified'
}

test_application_registry_is_root_owned_and_updater_read_only() {
  local configuration_directory="$temporary_directory/readonly-registry"
  local registry_file="$configuration_directory/updater/applications.ini"
  local dropin
  local state_step

  mkdir -p "$configuration_directory/updater"
  state_step="$(create_isolated_updater_state_step "$configuration_directory")"
  RUSTSERVER_UPDATER_SERVICE_USER="$(id -un)" RUSTSERVER_UPDATER_SERVICE_GROUP="$(id -gn)" \
    bash -c '
      source "$1"
      run_as_root() {
        if [[ "$1" == chown ]]; then
          [[ "$2" == root:root || "$2" == root:* ]] || exit 1
          return 0
        fi
        command "$@"
      }
      ensure_configuration_directory
      create_application_registry
    ' -- "$state_step"

  [[ "$(stat -c '%a' "$configuration_directory")" == '755' ]] || \
    fail 'Updater configuration directory is writable outside its root-owned policy mode'
  [[ "$(stat -c '%a' "$registry_file")" == '640' ]] || \
    fail 'applications.ini does not have mode 0640'
  dropin="$(bash -c 'source "$1"; source "$2"; render_updater_credential_dropin' -- \
    "$COMMON_LIBRARY" "$BOOTSTRAP_DIR/lib/updater-configuration.sh")"
  [[ "$dropin" == *'ReadWritePaths=/var/lib/rustserver/updater'* ]] || \
    fail 'Updater state directory is not the service write path'
  [[ "$dropin" != *'/etc/rustserver/updater '* ]] || \
    fail 'Updater service retains write access to its configuration directory'
}

test_bundled_systemd_units_are_runtime_self_contained() {
  local release_directory="$temporary_directory/bundle-systemd/release"
  local systemd_directory="$temporary_directory/bundle-systemd/systemd"
  local unit_name

  mkdir -p "$release_directory/api" "$release_directory/executor" \
    "$release_directory/systemd" "$systemd_directory"
  : > "$release_directory/api/RustServer.Updater.Api.dll"
  : > "$release_directory/executor/RustServer.Updater.Executor.dll"
  printf '%s\n' \
    '{"formatVersion":1,"releaseVersion":"v0.1.0-dev.2","updaterEntrypoint":"api/RustServer.Updater.Api.dll","executorEntrypoint":"executor/RustServer.Updater.Executor.dll"}' \
    > "$release_directory/rustserver-updater.bundle.json"
  for unit_name in rustserver-updater.service rustserver-updater-executor.socket \
    rustserver-updater-executor@.service rustserver-updater-recovery.service; do
    printf 'bundle:%s\n' "$unit_name" > "$release_directory/systemd/$unit_name"
  done

  RUSTSERVER_UPDATER_SYSTEMD_UNIT_DIRECTORY="$systemd_directory" bash -c '
    source "$1"
    source "$2"
    source "$3"
    source "$4"
    run_as_root() { command "$@"; }
    install_bundled_updater_systemd_units "$5" v0.1.0-dev.2
  ' -- "$COMMON_LIBRARY" "$BOOTSTRAP_DIR/lib/updater-configuration.sh" \
    "$BOOTSTRAP_DIR/lib/updater-bundle.sh" "$BOOTSTRAP_DIR/lib/updater-systemd.sh" "$release_directory"

  for unit_name in rustserver-updater.service rustserver-updater-executor.socket \
    rustserver-updater-executor@.service rustserver-updater-recovery.service; do
    cmp -- "$release_directory/systemd/$unit_name" "$systemd_directory/$unit_name" || \
      fail "Bootstrap did not install the bundled systemd unit: $unit_name"
  done
  grep -Fq 'Requires=rustserver-updater-recovery.service' "$BOOTSTRAP_DIR/lib/updater-systemd.sh" || \
    fail 'Updater service does not require the bundled recovery unit'
  grep -Fq '/opt/rustserver/updater/current/$UPDATER_API_RELATIVE_PATH' "$BOOTSTRAP_DIR/lib/updater-systemd.sh" || \
    fail 'Updater runtime drop-in does not execute the bundled API'
}

test_invalid_existing_updater_token_is_not_overwritten() {
  local configuration_directory="$temporary_directory/invalid-updater-token"
  local token_file="$configuration_directory/updater-github-token"
  local initial_digest
  local output
  local state_step

  mkdir -p "$configuration_directory/updater"
  state_step="$(create_isolated_updater_state_step "$configuration_directory")"
  printf 'invalid token with spaces\n' > "$token_file"
  chmod 0600 "$token_file"
  initial_digest="$(sha256sum "$token_file" | awk '{print $1}')"

  if output="$(bash -c '
    source "$1"
    run_as_root() {
      if [[ "$1" == stat ]]; then
        case "$3" in
          %U|%G) printf "%s\n" root ;;
          %a) printf "%s\n" 600 ;;
        esac
        return 0
      fi
      command "$@"
    }
    create_updater_token
  ' -- "$state_step" 2>&1)"; then
    fail 'invalid existing Updater token was accepted'
  fi

  [[ "$output" == *'Existing Updater GitHub token contains unsupported characters'* ]] || \
    fail 'invalid existing Updater token did not produce a clear diagnostic'
  assert_equals "$initial_digest" "$(sha256sum "$token_file" | awk '{print $1}')"
}

test_plugins_workspace_ownership_contract() {
  bash "$BOOTSTRAP_DIR/tests/plugins-credential-contract-tests.sh" ||
    fail 'Plugins GitHub credential/workspace ownership contract failed'
}

test_network_access_provisioning_contract() {
  bash -n "$PRODUCTION_SERVICES_PROVISIONER" ||
    fail 'NetworkAccess production provisioner has invalid shell syntax'

  for required_text in \
    'NFTABLES_PACKAGE=' \
    'IPROUTE2_PACKAGE=' \
    'KMOD_PACKAGE=' \
    'configure_nf_tables_module_loading' \
    'modprobe "$NF_TABLES_MODULE"' \
    'nft list tables' \
    'verify_control_release_boundary'; do
    grep -Fq -- "$required_text" "$PRODUCTION_SERVICES_PROVISIONER" ||
      fail "NetworkAccess Production provisioning is missing: $required_text"
  done

  grep -Fq 'User=rustserver-control' "$CONTROL_SERVICE_UNIT" ||
    fail 'Control must continue to run under its unprivileged service account'
  if grep -Eq 'ensure_network_access_helper_unit_link|enable_network_access_helper|NETWORK_ACCESS_HELPER_UNIT_LINK' "$PRODUCTION_SERVICES_PROVISIONER"; then
    fail 'Bootstrap must not install or enable the release-owned NetworkAccess helper'
  fi
  grep -Fq 'NoNewPrivileges=true' "$CONTROL_SERVICE_UNIT" ||
    fail 'Control must retain NoNewPrivileges'
  grep -Fq 'Requires=rustserver-network-access-helper.service' "$CONTROL_SERVICE_UNIT" ||
    fail 'Control does not require the NetworkAccess helper'
  grep -Fq 'After=network-online.target docker.service rustserver-network-access-helper.service' "$CONTROL_SERVICE_UNIT" ||
    fail 'Control does not start after the NetworkAccess helper'
  grep -Fq 'PartOf=docker.service' "$CONTROL_SERVICE_UNIT" ||
    fail 'Control does not reconcile NetworkAccess after a Docker restart'
}

test_terminal_tools_production_contract() {
  # Production Step 22 owns all sources and dependencies under deploy/bootstrap.
  bash -n "$TERMINAL_TOOLS_PRODUCTION_STEP" ||
    fail 'Production Step 22 Bash syntax is invalid'
  bash -n "$TERMINAL_TOOLS_REFERENCE" ||
    fail 'Production tools catalog Bash syntax is invalid'
  [[ -f "$TERMINAL_TOOLS_REFERENCE" ]] ||
    fail 'Production tools catalog is missing'
  grep -Fq '../assets/tools' "$TERMINAL_TOOLS_PRODUCTION_STEP" ||
    fail 'Production Step 22 must load its own assets'
  if grep -Eq '(\.\./)+scripts/|source .*bootstrap\.d|/scripts/bootstrap' "$TERMINAL_TOOLS_PRODUCTION_STEP"; then
    fail 'Production Step 22 must never depend on Development Bootstrap'
  fi

  # On a fully configured host, check happens before APT, sudo or networking.
  grep -Fq 'if all_terminal_tools_installed &&' "$TERMINAL_TOOLS_PRODUCTION_STEP" ||
    fail 'Production Step 22 is missing the early installed-tools check'
  grep -Fq "bootstrap_skip 'All terminal tools and help/info commands are already installed; no changes required.'" "$TERMINAL_TOOLS_PRODUCTION_STEP" ||
    fail 'Production Step 22 is missing the no-op SKIP'
  grep -Fq 'missing_apt_packages+=' "$TERMINAL_TOOLS_PRODUCTION_STEP" ||
    fail 'Production Step 22 must install only missing APT packages'

  for required in termide mc lazygit lazydocker bat rg jq micro btop tmux ncdu fzf duf lnav systemctl-tui lazyjournal yazi hx posting; do
    grep -Fq -- "$required" "$TERMINAL_TOOLS_PRODUCTION_STEP" ||
      fail "Production Step 22 does not cover tool: $required"
  done

  # Actual reference output is limited to installed commands.
  local fake_path="$temporary_directory/terminal-tool-test-bin"
  local output
  mkdir -p "$fake_path"
  ln -s /bin/true "$fake_path/mc"
  ln -s /bin/true "$fake_path/rg"
  output="$(PATH="$fake_path" /bin/bash "$TERMINAL_TOOLS_REFERENCE")"
  [[ "$output" == *'Две панели'* && "$output" == *'Быстро искать'* ]] ||
    fail 'Production catalog did not display available commands'
  [[ "$output" != *'btop'* && "$output" != *'не установлено'* ]] ||
    fail 'Production catalog displayed an uninstalled command'
  if /bin/bash "$TERMINAL_TOOLS_REFERENCE" --all >/dev/null 2>&1; then
    fail 'Production catalog must not expose a --all option'
  fi
}

run_test test_terminal_tools_production_contract
run_test test_orchestration_order_and_logs
run_test test_plugins_workspace_ownership_contract
run_test test_orchestration_stops_on_failure
run_test test_bootstrap_defaults_are_version_controlled
run_test test_default_install_mode_rejects_unsupported_modes
run_test test_release_channel_selection_and_semver_ordering
run_test test_no_matching_or_invalid_release_metadata_is_rejected
run_test test_channel_is_resolved_before_updater_installation
run_test test_initial_release_selection_is_transferred_between_processes
run_test test_bootstrap_lock_rejects_concurrent_operation
run_test test_staticweb_nginx_contract_is_root_owned_and_release_aware
run_test test_landing_install_preserves_existing_state
run_test test_plugin_catalog_platform_boundary_contract
run_test test_legacy_catalog_topology_is_preserved_and_ignored
run_test test_auth_registration_migration_and_first_provisioning
run_test test_plugin_catalog_registration_migration_is_additive_and_safe
run_test test_invalid_auth_environment_is_not_overwritten
run_test test_auth_handoff_requires_google_and_first_admin_values
run_test test_rcon_first_provisioning_preserves_existing_unit
run_test test_rcon_docker_secret_acl_is_exact_and_idempotent
run_test test_persistent_updater_state_does_not_overwrite_existing_files
run_test test_clean_updater_state_initialization
run_test test_configuration_is_data_not_shell_code
run_test test_runtime_environment_value_lookup
run_test test_published_bundle_layout
run_test test_failed_checksum_is_rejected_before_switching
run_test test_failed_updater_download_is_rejected_before_switching
run_test test_repeated_install_preserves_the_active_updater_release
run_test test_protected_environment_supports_safe_reruns
run_test test_protected_bootstrap_defaults_are_rejected_without_modification
run_test test_application_registry_is_root_owned_and_updater_read_only
run_test test_bundled_systemd_units_are_runtime_self_contained
run_test test_network_access_provisioning_contract
run_test test_invalid_existing_updater_token_is_not_overwritten

if [[ -n "${RUSTSERVER_BOOTSTRAP_TEST:-}" && "$BOOTSTRAP_TESTS_RUN" == '0' ]]; then
  fail "unknown Bootstrap test: $RUSTSERVER_BOOTSTRAP_TEST"
fi

printf 'OK: Bootstrap tests passed\n'
