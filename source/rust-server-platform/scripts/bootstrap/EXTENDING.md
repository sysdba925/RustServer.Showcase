# Extending bootstrap

## Add a service source repository

These instructions apply **only to service code repositories** managed by
Development Bootstrap. `RustServer.ConfigFiles` and `RustServer.PluginFiles`
are **not** Bootstrap repository sync targets: RustServer.Plugins creates and
manages their Git workspaces using its own systemd-provided GitHub token.
Do not add steps to clone, fetch, checkout, or synchronize these two source
repositories in Bootstrap.

## Add a repository

- Add `NN-sync-<repo>.sh` under `scripts/bootstrap.d/`.
- Declare the repository name, SSH URL, and workspace target directory in that script.
- Use `scripts/lib/git.sh` for generic safe clone/fetch/fast-forward behavior when appropriate.
- Do not add repository-specific paths or URLs to global registries.

```bash
#!/usr/bin/env bash
set -Eeuo pipefail

readonly STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly PLATFORM_DIR="$(cd -- "$STEP_DIR/../.." && pwd -P)"
readonly WORKSPACE_ROOT="$(cd -- "$PLATFORM_DIR/.." && pwd -P)"
readonly REPOSITORY_NAME="Example.Repository"
readonly REPOSITORY_DIR="$WORKSPACE_ROOT/repos/$REPOSITORY_NAME"
readonly REPOSITORY_URL="git@github.com:owner/Example.Repository.git"

source "$STEP_DIR/../lib/common.sh"
source "$STEP_DIR/../lib/git.sh"

sync_git_repository "$REPOSITORY_NAME" "$REPOSITORY_DIR" "$REPOSITORY_URL"
```

## Add a build step

- Add a separate `NN-build-<repo>.sh` step.
- Declare its repository path, solution/project path, and required tools locally.
- Keep restore/build/test behavior and validation of its result in that step.

## Add a NuGet-producing repository

- Restore, build, test, and pack in the producer step.
- Validate the generated `.nupkg`.
- Place it in the workspace local NuGet feed: `packages/nuget`.
- Handle the producer's exact package ID/version and workspace cache invalidation there.
- Run consuming repository build steps later; they must restore from the workspace feed/cache.

## Validation

- Repository-specific and artifact-specific validation belongs in the step that owns the operation.
- Do not add repository/package-specific knowledge to `99-validate-bootstrap.sh`.

## Shared code

Use shared libraries only for genuinely generic mechanics:

- logging and errors;
- generic command/file checks;
- generic safe Git operations.

Prefer small local duplication over hidden cross-script dependencies.

## Extension and safety

- Before changing configuration, Bootstrap, systemd, Docker Compose, or service
  integration, read the approved
  [`runtime configuration layout`](../../docs/architecture/runtime-configuration-layout.md).
  Treat its target paths as a pending migration contract until the corresponding
  runtime change has been implemented and validated.
- Add/copy numbered scripts instead of modifying `bootstrap.sh`, shared registries, or unrelated steps.
- Preserve fail-fast behavior and idempotent reruns where practical.
- Never use destructive Git recovery.
- Never log secrets; preserve existing local configuration and secrets.
- Coding agents must not commit or push.
