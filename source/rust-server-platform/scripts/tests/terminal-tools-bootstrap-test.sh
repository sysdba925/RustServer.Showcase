#!/usr/bin/env bash
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly TOOLS="$SCRIPT_DIR/../bootstrap/assets/tools"
readonly STEP="$SCRIPT_DIR/../bootstrap.d/22-install-terminal-tools.sh"

bash -n "$TOOLS"
bash -n "$STEP"

# One-command catalog: no missing-program list or --all mode.
if bash "$TOOLS" --all >/dev/null 2>&1; then
  echo 'tools unexpectedly accepted --all' >&2
  exit 1
fi

tmp="$(mktemp -d)"
trap 'rm -rf -- "$tmp"' EXIT
mkdir -p "$tmp/bin"
ln -s /bin/true "$tmp/bin/mc"
ln -s /bin/true "$tmp/bin/rg"
output="$(PATH="$tmp/bin" /bin/bash "$TOOLS")"
grep -Eq '(^|[[:space:]])mc[[:space:]]+Две панели' <<< "$output" || { echo 'missing installed mc' >&2; exit 1; }
grep -Eq '(^|[[:space:]])rg[[:space:]]+Быстро искать' <<< "$output" || { echo 'missing installed rg' >&2; exit 1; }
[[ "$output" != *"btop"* ]] || { echo 'uninstalled btop must not be shown' >&2; exit 1; }
[[ "$output" != *"не установлено"* ]] || { echo 'missing-tool label must not be shown' >&2; exit 1; }

# The step must return before package operations once its prerequisites persist.
grep -Fq "if all_terminal_tools_installed &&" "$STEP"
grep -Fq "bootstrap_skip 'All terminal tools and help/info commands are already installed; no changes required.'" "$STEP"
grep -Fq 'missing_apt_packages+=' "$STEP"
printf 'Terminal tools Bootstrap tests passed.\n'
