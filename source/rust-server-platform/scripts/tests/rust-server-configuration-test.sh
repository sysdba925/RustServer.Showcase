#!/usr/bin/env bash
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"
# shellcheck source=../lib/rust-server-configuration.sh
source "$PLATFORM_DIR/scripts/lib/rust-server-configuration.sh"

run_as_root() {
  command "$@"
}

fail() {
  printf 'Test failure: %s\n' "$*" >&2
  exit 1
}

assert_file_equals() {
  local expected="$1"
  local actual="$2"

  cmp -s -- "$expected" "$actual" || fail "Files differ: $expected and $actual"
}

create_configuration() {
  local path="$1"
  local server_path="$2"

  printf 'RUST_SERVER_PATH=%s\nRUST_SERVER_IDENTITY=rust-server\n' "$server_path" > "$path"
}

readonly TEST_DIRECTORY="$(mktemp -d)"
trap 'rm -rf -- "$TEST_DIRECTORY"' EXIT
readonly TEMPLATE_FILE="$TEST_DIRECTORY/.env.example"
cp -- "$PLATFORM_DIR/docker/rust-server/.env.example" "$TEMPLATE_FILE"

# Fresh configuration creation from the checked-in template equivalent.
fresh_target="$TEST_DIRECTORY/fresh/rust-server.env"
rust_server_migrate_configuration "$TEST_DIRECTORY/missing.env" "$fresh_target" "$TEMPLATE_FILE"
assert_file_equals "$TEMPLATE_FILE" "$fresh_target"

# A valid target is left byte-for-byte unchanged.
existing_target="$TEST_DIRECTORY/existing/rust-server.env"
mkdir -p -- "$(dirname -- "$existing_target")"
create_configuration "$existing_target" '/custom/rust/server'
existing_digest_before="$(sha256sum -- "$existing_target")"
rust_server_migrate_configuration "$TEST_DIRECTORY/absent.env" "$existing_target" "$TEMPLATE_FILE"
[[ "$(sha256sum -- "$existing_target")" == "$existing_digest_before" ]] || fail 'Existing target was modified'

# Legacy values move to the target, while the legacy file remains recoverable.
legacy_file="$TEST_DIRECTORY/legacy.env"
migrated_target="$TEST_DIRECTORY/migrated/rust-server.env"
create_configuration "$legacy_file" '/data/rust/server'
rust_server_migrate_configuration "$legacy_file" "$migrated_target" "$TEMPLATE_FILE"
assert_file_equals "$legacy_file" "$migrated_target"
[[ -f "$legacy_file" ]] || fail 'Legacy configuration was not retained'

# Different legacy and canonical files fail without modifying either one.
conflict_legacy="$TEST_DIRECTORY/conflict-legacy.env"
conflict_target="$TEST_DIRECTORY/conflict/rust-server.env"
mkdir -p -- "$(dirname -- "$conflict_target")"
create_configuration "$conflict_legacy" '/legacy/rust/server'
create_configuration "$conflict_target" '/canonical/rust/server'
conflict_legacy_digest="$(sha256sum -- "$conflict_legacy")"
conflict_target_digest="$(sha256sum -- "$conflict_target")"
if rust_server_migrate_configuration "$conflict_legacy" "$conflict_target" "$TEMPLATE_FILE"; then
  fail 'Conflicting configurations unexpectedly succeeded'
fi
[[ "$(sha256sum -- "$conflict_legacy")" == "$conflict_legacy_digest" ]] || fail 'Legacy conflict file was modified'
[[ "$(sha256sum -- "$conflict_target")" == "$conflict_target_digest" ]] || fail 'Canonical conflict file was modified'

# Repeated migration is a no-op once both files match.
migrated_digest_before="$(sha256sum -- "$migrated_target")"
rust_server_migrate_configuration "$legacy_file" "$migrated_target" "$TEMPLATE_FILE"
[[ "$(sha256sum -- "$migrated_target")" == "$migrated_digest_before" ]] || fail 'Idempotent migration changed the canonical file'

printf 'Rust server configuration migration tests passed.\n'
