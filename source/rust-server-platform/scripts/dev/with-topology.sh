#!/usr/bin/env bash
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=../lib/topology.sh
source "$SCRIPT_DIR/../lib/topology.sh"

if (( $# == 0 )); then
  printf 'Usage: %s <command> [arguments...]\n' "${0##*/}" >&2
  exit 2
fi

load_topology_environment
exec "$@"
