#!/usr/bin/env bash
# Static integration checks. No root privileges or live credentials required.
set -Eeuo pipefail
test_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
bootstrap_dir="$(cd -- "$test_dir/.." && pwd -P)"
platform_dir="$(cd -- "$bootstrap_dir/../.." && pwd -P)"
production="$bootstrap_dir/provision-services.sh"
development="$platform_dir/scripts/bootstrap.d/93-install-rustserver-plugins-service.sh"
installer="$platform_dir/deploy/install.sh"
development_steps="$platform_dir/scripts/bootstrap.d"
development_bootstrap="$platform_dir/scripts/bootstrap.sh"


for script in "$production" "$development" "$installer"; do
  bash -n "$script" || { printf 'FAIL: shell syntax: %s\n' "$script" >&2; exit 1; }
done

for script in "$production" "$development"; do
  grep -Fq 'plugins-github-token' "$script" ||
    { printf 'FAIL: Plugins token source is missing: %s\n' "$script" >&2; exit 1; }
  grep -Fq 'LoadCredential=github-token:/etc/rustserver/plugins-github-token' "$script" ||
    { printf 'FAIL: LoadCredential is missing: %s\n' "$script" >&2; exit 1; }
  grep -Fq 'RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE=%d/github-token' "$script" ||
    { printf 'FAIL: credential-path environment is missing: %s\n' "$script" >&2; exit 1; }
  grep -Fq 'PluginManagement__WorkspaceInitialization__Enabled=true' "$script" ||
    { printf 'FAIL: automatic workspace initialization is disabled: %s\n' "$script" >&2; exit 1; }
done

grep -Fq 'Preserving existing Plugins GitHub credential' "$production" ||
  { printf 'FAIL: production Bootstrap lost token preservation\n' >&2; exit 1; }
grep -Fq 'PLUGINS_GITHUB_TOKEN_FILE' "$installer" ||
  { printf 'FAIL: Installer does not accept separate Plugins token input\n' >&2; exit 1; }
grep -Fq 'RUSTSERVER_BOOTSTRAP_PLUGINS_GITHUB_TOKEN_FILE' "$installer" ||
  { printf 'FAIL: Installer does not pass secure Plugins token handoff\n' >&2; exit 1; }
grep -Fq "read_masked_input 'GitHub plugins token: '" "$installer" ||
  { printf 'FAIL: Installer does not prompt for dedicated Plugins GitHub token\n' >&2; exit 1; }
grep -Fq 'source_file="${RUSTSERVER_BOOTSTRAP_PLUGINS_GITHUB_TOKEN_FILE:-}"' "$production" ||
  { printf 'FAIL: Production Bootstrap does not require explicit Plugins token handoff\n' >&2; exit 1; }
grep -Fq 'A dedicated Plugins GitHub token is required on first install' "$production" ||
  { printf 'FAIL: Missing Plugins token does not stop Production Bootstrap\n' >&2; exit 1; }
if grep -Fq '/etc/rustserver/updater-github-token' "$production"; then
  printf 'FAIL: Production Bootstrap still allows Updater token fallback for Plugins\n' >&2
  exit 1
fi
# The Development orchestrator must not contain any repository-sync entry point
# for Plugins-owned source workspaces. The service owns these checkouts.
for obsolete in \
  "$development_steps/61-sync-rustserver-configfiles.sh" \
  "$development_steps/62-sync-rustserver-pluginfiles.sh"; do
  if [[ -e "$obsolete" || -L "$obsolete" ]]; then
    printf 'FAIL: obsolete Plugins-owned workspace sync step remains: %s\n' "$obsolete" >&2
    exit 1
  fi
done

grep -Fq 'PluginManagement__Repositories__Configs__WorkspacePath=' "$development" ||
  { printf 'FAIL: Plugins Configs workspace path is not supplied\n' >&2; exit 1; }
grep -Fq 'PluginManagement__Repositories__Plugins__WorkspacePath=' "$development" ||
  { printf 'FAIL: Plugins PluginFiles workspace path is not supplied\n' >&2; exit 1; }

if grep -Eq '(verify_workspace_access|sync_git_repository|git -C.*oxide)' "$development"; then
  printf 'FAIL: Plugins installer still clones or validates a pre-existing Git checkout\n' >&2
  exit 1
fi

grep -Fq 'A Plugins GitHub token is required for first-run Clone' "$development" ||
  { printf 'FAIL: missing token does not fail closed for first-run Clone\n' >&2; exit 1; }

# Service repository sync steps must remain intact: only the two source
# Git repositories have been removed from Bootstrap ownership.
test -f "$development_steps/63-sync-rustserver-plugins.sh" ||
  { printf 'FAIL: the Plugins *service-code* repository sync was removed\n' >&2; exit 1; }
grep -Fq 'sync_git_repository' "$development_steps/63-sync-rustserver-plugins.sh" ||
  { printf 'FAIL: service-code Git sync no longer works\n' >&2; exit 1; }
bash -n "$development_bootstrap"
bash -n "$development"

printf 'OK: Plugins credential bootstrap source contracts passed\n'
