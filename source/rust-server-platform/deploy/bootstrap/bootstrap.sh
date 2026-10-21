#!/usr/bin/env bash
# Discovers and runs production Bootstrap steps in numeric order.
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly SCRIPT_DIR
# shellcheck source=lib/common.sh
source "$SCRIPT_DIR/lib/common.sh"

readonly BOOTSTRAP_STEPS_DIR="${RUSTSERVER_BOOTSTRAP_STEPS_DIR:-$SCRIPT_DIR/steps}"

validate_bootstrap_invocation
acquire_bootstrap_lock

[[ -d "$BOOTSTRAP_STEPS_DIR" ]] || \
  bootstrap_die "Bootstrap steps directory is missing: $BOOTSTRAP_STEPS_DIR"

mapfile -t bootstrap_steps < <(find "$BOOTSTRAP_STEPS_DIR" -maxdepth 1 -type f -name '[0-9]*-*.sh' -printf '%f\n' | LC_ALL=C sort -V)
(( ${#bootstrap_steps[@]} > 0 )) || \
  bootstrap_die "No numbered Bootstrap steps were found in: $BOOTSTRAP_STEPS_DIR"

for step_file in "${bootstrap_steps[@]}"; do
  step_name="${step_file%.sh}"
  step_path="$BOOTSTRAP_STEPS_DIR/$step_file"

  bootstrap_log "RUN" "$step_name"
  if bash "$step_path"; then
    bootstrap_log "OK" "$step_name"
  else
    exit_code=$?
    bootstrap_log "FAIL" "$step_name (exit $exit_code)" >&2
    exit "$exit_code"
  fi
done

bootstrap_log "RUN" "provision-services"
if bash "$SCRIPT_DIR/provision-services.sh"; then
  bootstrap_log "OK" "provision-services"
else
  exit_code=$?
  bootstrap_log "FAIL" "provision-services (exit $exit_code)" >&2
  exit "$exit_code"
fi

bootstrap_log "OK" "Bootstrap completed successfully"
