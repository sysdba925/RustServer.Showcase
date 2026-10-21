#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly RUNTIME_PARENT_DIRECTORY="/opt/rustserver"
readonly RUNTIME_DIRECTORY="$RUNTIME_PARENT_DIRECTORY/runtime"
readonly RUST_SERVER_RUNTIME_DIRECTORY="$RUNTIME_DIRECTORY/rust-server"
readonly STEAMCMD_RUNTIME_DIRECTORY="$RUNTIME_DIRECTORY/steamcmd"
readonly OXIDE_RUNTIME_DIRECTORY="$RUNTIME_DIRECTORY/oxide"
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

run_as_root() {
  if (( EUID == 0 )); then
    command "$@"
  else
    sudo "$@"
  fi
}

ensure_runtime_parent_directory() {
  local directory="$1"

  if [[ -e "$directory" || -L "$directory" ]]; then
    [[ -d "$directory" && ! -L "$directory" ]] || \
      bootstrap_die "Runtime path is not a regular directory: $directory"
    return
  fi

  run_as_root install -d -o root -g root -m 0755 -- "$directory" || \
    bootstrap_die "Failed to create runtime directory: $directory"
  bootstrap_info "Created runtime directory: $directory"
}

ensure_runtime_directory() {
  local directory="$1"
  local owner_and_mode

  if [[ -e "$directory" || -L "$directory" ]]; then
    [[ -d "$directory" && ! -L "$directory" ]] || \
      bootstrap_die "Runtime path is not a regular directory: $directory"
  fi

  owner_and_mode="$(run_as_root stat -c '%U:%G:%a' -- "$directory" 2>/dev/null || true)"
  if [[ "$owner_and_mode" == 'root:root:755' ]]; then
    bootstrap_skip "Runtime directory is already current: $directory"
    return
  fi

  run_as_root install -d -o root -g root -m 0755 -- "$directory" || \
    bootstrap_die "Failed to create or secure runtime directory: $directory"
  bootstrap_info "Prepared runtime directory: $directory"
}

install_runtime_asset() {
  local source_path="$1"
  local destination_path="$2"
  local mode="$3"
  local destination_owner_and_mode

  [[ -f "$source_path" && ! -L "$source_path" ]] || \
    bootstrap_die "Runtime asset source is not a regular file: $source_path"

  if [[ -e "$destination_path" || -L "$destination_path" ]]; then
    [[ -f "$destination_path" && ! -L "$destination_path" ]] || \
      bootstrap_die "Installed runtime asset is not a regular file: $destination_path"
  fi

  destination_owner_and_mode="$(run_as_root stat -c '%U:%G:%a' -- "$destination_path" 2>/dev/null || true)"
  if run_as_root test -f "$destination_path" && \
    run_as_root cmp -s -- "$source_path" "$destination_path" && \
    [[ "$destination_owner_and_mode" == "root:root:$mode" ]]; then
    bootstrap_skip "Runtime asset is already current: $destination_path"
    return
  fi

  run_as_root install -o root -g root -m "$mode" -- "$source_path" "$destination_path" || \
    bootstrap_die "Failed to install runtime asset: $destination_path"
  bootstrap_info "Installed runtime asset: $destination_path"
}

validate_installed_compose_asset() {
  local compose_file="$1"
  local script_file="$2"
  local mount_declaration="$3"

  [[ -f "$compose_file" && ! -L "$compose_file" ]] || \
    bootstrap_die "Installed Compose definition is not a regular file: $compose_file"
  [[ -f "$script_file" && ! -L "$script_file" ]] || \
    bootstrap_die "Installed Compose script is not a regular file: $script_file"
  grep -Fqx -- "$mount_declaration" "$compose_file" || \
    bootstrap_die "Installed Compose definition does not mount its colocated script: $compose_file"
}

for required_command in cmp grep install stat; do
  require_command "$required_command"
done

ensure_sudo_access

ensure_runtime_parent_directory "$RUNTIME_PARENT_DIRECTORY"

for runtime_directory in \
  "$RUNTIME_DIRECTORY" \
  "$RUST_SERVER_RUNTIME_DIRECTORY" \
  "$RUST_SERVER_RUNTIME_DIRECTORY/scripts" \
  "$STEAMCMD_RUNTIME_DIRECTORY" \
  "$STEAMCMD_RUNTIME_DIRECTORY/scripts" \
  "$OXIDE_RUNTIME_DIRECTORY" \
  "$OXIDE_RUNTIME_DIRECTORY/scripts"; do
  ensure_runtime_directory "$runtime_directory"
done

install_runtime_asset \
  "$PLATFORM_DIR/docker/rust-server/compose.yml" \
  "$RUST_SERVER_RUNTIME_DIRECTORY/compose.yml" \
  644
install_runtime_asset \
  "$PLATFORM_DIR/docker/rust-server/scripts/start-rust.sh" \
  "$RUST_SERVER_RUNTIME_DIRECTORY/scripts/start-rust.sh" \
  755
install_runtime_asset \
  "$PLATFORM_DIR/docker/steamcmd/compose.yml" \
  "$STEAMCMD_RUNTIME_DIRECTORY/compose.yml" \
  644
install_runtime_asset \
  "$PLATFORM_DIR/docker/steamcmd/scripts/update-rust.sh" \
  "$STEAMCMD_RUNTIME_DIRECTORY/scripts/update-rust.sh" \
  755
install_runtime_asset \
  "$PLATFORM_DIR/docker/oxide/compose.yml" \
  "$OXIDE_RUNTIME_DIRECTORY/compose.yml" \
  644
install_runtime_asset \
  "$PLATFORM_DIR/docker/oxide/scripts/update-oxide.sh" \
  "$OXIDE_RUNTIME_DIRECTORY/scripts/update-oxide.sh" \
  755

validate_installed_compose_asset \
  "$RUST_SERVER_RUNTIME_DIRECTORY/compose.yml" \
  "$RUST_SERVER_RUNTIME_DIRECTORY/scripts/start-rust.sh" \
  '      - "./scripts/start-rust.sh:/scripts/start-rust.sh:ro"'
validate_installed_compose_asset \
  "$STEAMCMD_RUNTIME_DIRECTORY/compose.yml" \
  "$STEAMCMD_RUNTIME_DIRECTORY/scripts/update-rust.sh" \
  '      - "./scripts/update-rust.sh:/scripts/update-rust.sh:ro"'
validate_installed_compose_asset \
  "$OXIDE_RUNTIME_DIRECTORY/compose.yml" \
  "$OXIDE_RUNTIME_DIRECTORY/scripts/update-oxide.sh" \
  '      - "./scripts/update-oxide.sh:/scripts/update-oxide.sh:ro"'

bootstrap_info "Installed Docker Compose runtime assets are ready at $RUNTIME_DIRECTORY"
