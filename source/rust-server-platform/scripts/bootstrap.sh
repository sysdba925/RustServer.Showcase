#!/usr/bin/env bash
# Ordered bootstrap orchestrator. Implementation belongs in bootstrap.d steps.
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=lib/common.sh
source "$SCRIPT_DIR/lib/common.sh"

readonly BOOTSTRAP_STEPS_DIR="$SCRIPT_DIR/bootstrap.d"

if [[ ! -d "$BOOTSTRAP_STEPS_DIR" ]]; then
  bootstrap_die "Bootstrap steps directory is missing: $BOOTSTRAP_STEPS_DIR"
fi

mapfile -t bootstrap_steps < <(find "$BOOTSTRAP_STEPS_DIR" -maxdepth 1 -type f -name '[0-9]*-*.sh' -printf '%f\n' | LC_ALL=C sort -V)

if (( ${#bootstrap_steps[@]} == 0 )); then
  bootstrap_die "No numbered bootstrap steps were found in: $BOOTSTRAP_STEPS_DIR"
fi

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

bootstrap_log "OK" "Bootstrap completed successfully"
