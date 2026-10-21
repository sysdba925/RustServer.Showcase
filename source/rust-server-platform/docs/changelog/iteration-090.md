# Iteration 090 — Bootstrap no longer manages Plugins source Git repositories

## Goal

Complete the clean-development-host cutover for Git workspace ownership.
`RustServer.Plugins` is the only component that initializes and manages
`RustServer.ConfigFiles` and `RustServer.PluginFiles` Git checkouts.
No SSH-to-HTTPS migration is needed: the development VM is rebuilt from
scratch and there is no existing Production installation to migrate.

## Changes

- Deleted obsolete Development Bootstrap steps
  `61-sync-rustserver-configfiles.sh` and
  `62-sync-rustserver-pluginfiles.sh`, including their stop/start logic
  and SSH origins.
- Removed `verify_workspace_access` in step 93, which previously required
  existing `.git` directories and ran Git before starting the service.
- Step 93 now checks only that the non-root service user can access the
  shared workspace parent. It supplies the canonical paths and starts
  Plugins with `PluginManagement__WorkspaceInitialization__Enabled=true`.
- For a clean Development Bootstrap, a Plugins token is now required:
  use `RUSTSERVER_BOOTSTRAP_PLUGINS_GITHUB_TOKEN_FILE`, a pre-existing
  `/etc/rustserver/plugins-github-token`, or a pre-existing Updater token
  which Bootstrap copies to the independent Plugins file.
  Without any token, Bootstrap fails explicitly instead of starting a
  service that cannot perform the initial HTTPS Clone.
- The `root:root` `0600` credential preservation, systemd
  `LoadCredential`, and service workspace path configuration remain
  unchanged.
- Updated Development and Production Bootstrap README/OVERVIEW/EXTENDING
  to remove old ownership and synchronization instructions.
- Extended the Bootstrap credential regression script to assert both
  legacy steps remain absent, the service-code Git sync step is preserved,
  and step 93 never checks or synchronizes pre-cloned source workspaces.
  Added this check to the standard Production Bootstrap shell test suite.

## Boundaries

- No SSH keys or `~/.ssh` files are deleted, changed or inspected.
- Other microservice *code* repositories (including the
  `RustServer.Plugins` service repository) continue using their own
  existing Bootstrap Git synchronization.
- Production Bootstrap continues preparing persistent workspace directories
  and credentials, but does not clone Git source workspaces.
- No changes to the `RustServer.Plugins` repository were needed for
  this cleanup.

## Validation

- Confirmed both legacy steps are absent from `develop` and the updated
  step 93 no longer calls `git` to validate their worktrees.
- Confirmed canonical workspace paths and systemd credential wiring remain.
- Added regression tests; full Bash test suite and a clean Ubuntu VM run
  **have not been executed in this tool environment**.
- End-to-end authenticated Clone/Fetch/Push remains subject to fresh-VM
  validation.

## Production Installer follow-up: three dedicated GitHub tokens

- First-run Installer now collects **three** independent credentials, in this order:
  Bootstrap (read `rust-server-platform` and `RustServer.Updater`),
  Updater (read GitHub Releases for all managed services), and Plugins
  (read/write **only** `RustServer.PluginFiles` and `RustServer.ConfigFiles`).
  Google and initial administrator credentials are requested afterwards.
- `PLUGINS_GITHUB_TOKEN` / `PLUGINS_GITHUB_TOKEN_FILE` can provide the
  dedicated third token non-interactively. If it is missing on a fresh host,
  the Installer prompts (or fails explicitly for non-interactive execution).
- Production Bootstrap no longer copies `updater-github-token` as a fallback
  when provisioning `plugins-github-token`: a dedicated source is required.
  Existing secure files remain authoritative and are preserved on reinstalls.
- Added regression checks for missing/supplied/existing Plugins credentials,
  independent Bootstrap handoff, and removal of the shared-token fallback.
- Updated the Production installation guide and Bootstrap README to describe
  the permissions and three prompts.
- **Validation:** the changed GitHub files and test assertions were reviewed
  statically. Full Bash test suite and a fresh Ubuntu interactive Installer
  run have not been executed in this environment. Repository permissions are
  configured in GitHub and are not inferred from token strings.

## Plugins Git clone diagnostic follow-up

- Added a diagnostics-only change in `RustServer.Plugins` (PR #1) for persistent first-start Git Clone failures observed on a clean Production VM: the repository identifier, Git exit code and a **fixed, secret-free category** are included in service logs. Raw Git stderr is captured in memory but is never printed because it can contain sensitive URLs or credentials.
- No Git URLs, HTTPS/SSH selection, workspace initialization behavior, API contracts, or Rust lifecycle policies change.
- **Bootstrap reviewed — no change needed:** Production Bootstrap already creates the two workspaces, owns systemd `LoadCredential` provisioning and paths; this fix only changes in-process error reporting.
- **Expanded temporary diagnostics (2026-10-15):** a clean Production host still reports `Configs` Clone exit code `128` with `askpassExecutionFailed`, while independent Clone and executable tests under the service account succeed. The Plugins `develop` branch now logs authenticated Git command preflight/completion, service user and process ID, workspace/staging paths, credential-file presence (never contents), token-redacted Git stderr, and exact generated askpass script metadata (Unix permissions, first-line hex, CRLF/shebang validity).
- The diagnostics are temporary and intentionally verbose. They must be removed or reduced after the production startup failure is understood.
- **Validation pending:** unit/regression tests for redaction and script metadata were added, but .NET build/full test execution and fresh-VM verification are still required before publishing the next dev release.

## Plugins Environment namespace and askpass diagnostic correction

- `RustServer.Plugins` develop commit `21d37f5565938c42270ca828374f82d6b393a123` moves the three persisted Environment state types from `Features/PluginManagement/Environment` to `Infrastructure/EnvironmentState`. This avoids the source namespace named `Environment` shadowing the .NET `System.Environment` type. Business models, API paths and operator workflows stay unchanged.
- The same distinct commit removes calls to `GitHubAskPassSession.DescribeForDiagnostics()` that had caused an observed local `CS1061` compile error. Diagnostics are now calculated by `GitProcess` through the pre-existing askpass `Configure` contract.
- **Production Bootstrap reviewed, no changes required:** no service file layout, credential configuration, deployment output, or runtime paths changed.
- **Validation pending:** code/tree refs verified in GitHub; .NET build and automated tests must still run locally before publishing the next Plugins dev release.

## Plugins Git askpass CRLF root cause and fix

- Confirmed the original failure on Production VM `rust-clean` using `v0.1.0-dev.7`: Configs clone returned Git exit code 128 with `cannot exec .../askpass.sh: No such file or directory`, while the script itself existed. Its shebang bytes were `23212F62696E2F73680D` (`#!/bin/sh\r`), proving Windows CRLF in the generated script caused Linux to look for a non-existent interpreter.
- `RustServer.Plugins` `develop` commit `8497a1e91b5ad611b03feb09e6e2360074f1f64a` normalizes the temporary askpass content with `ReplaceLineEndings("\\n")` before writing. The executable receives LF regardless of Windows source checkout line endings.
- Regression tests cover CRLF/LF/CR input and actual Linux helper shebang creation. The authenticated Git command, systemd credential, and repository ownership protocols remain unchanged.
- **Bootstrap reviewed, no changes required:** this was a runtime script serialization issue, not a systemd unit, install path, permission, or credential configuration problem.
- **Validation pending:** .NET build/full tests and live Linux checkout on the next Plugins dev release remain to be run. Remove the temporary verbose diagnostics after successful verification.

## Plugins Runtime Diff respects source .gitignore (managed-file safety)

- `RustServer.Plugins/develop` now excludes Git-ignored untracked Runtime paths from the managed file set, before calculating `Runtime Diff`. This corrects the clean-VM failure where player/server files ignored by `RustServer.ConfigFiles/.gitignore` were presented as `Added` and could make `Commit + Push` fail during `stageConfigs`.
- The scope is evaluated by read-only `git check-ignore --stdin -z` for the current checked-out source, honoring nested `.gitignore` and negation patterns. Already-tracked paths remain managed. Git ignore query failures abort safely instead of showing all files.
- The same scope is reused by Runtime Diff, Preview, Apply, full/selected Revert and Runtime rollback snapshots. Ignored server-generated files are not included as changes, staged, reverted or deleted as obsolete managed files.
- API response shape is unchanged; behavioral specifications in Plugins `requirements.md`, `operations.md`, and `api-contract.md` were updated. Regression tests cover ignored data, whitelisted exceptions, tracked files, multi-batch queries, Apply and Revert preservation.
- **Bootstrap reviewed; no changes required:** repository paths, ownership, credentials, process binaries and deployment configuration remain unchanged.
- **Validation:** Git `check-ignore --stdin -z` semantics were checked against a local repository; .NET build, full test suite and clean-VM validation remain outstanding.

## Plugins cleanup: remove temporary Git diagnostics after validation

- Following successful Linux validation of `RustServer.Plugins` Git initialization and Runtime Commit + Push, removed the temporary verbose Git authentication and clone logs in Plugins `develop` commit `605c9cc7f50e6ca04c554d3258bcca426a7eeec1`.
- Removed per-command preflight/completion logs, temporary script path/mode/shebang dumps, raw token-redacted Git stderr logging, and obsolete diagnostic helpers/tests. Kept a concise non-secret workspace initialization failure report with repository, exit code and fixed diagnostic category.
- Preserved the permanent LF normalization of generated GitHub askpass helpers, Git ignore-based managed Runtime filtering, credential handling, Git command flow, and regression tests for those behaviors. No HTTP/API contract changes.
- **Bootstrap reviewed:** no install, systemd, permissions, paths or health-check changes required. GitHub code revision has been inspected; build, automated tests and subsequent Linux rollout remain for validation.
