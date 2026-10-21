# Iteration 096 — Live Oxide plugin management without stopping Rust

## Goal

Remove the artificial stopped-server precondition from `RustServer.Plugins`
commands. The Plugins service manages Git sources and Oxide plugin/config/data
files rather than the Rust process lifecycle. Operators can Commit + Push
Runtime changes, Apply selected Source, Revert, Fetch, Git Sync and switch
releases while Rust is running; the platform does not predict or verify the
resulting plugin reload behavior.

## Added and updated

- Reworked the authoritative Plugins documentation and agent rules:
  `docs/requirements.md`, `docs/operations.md`, `docs/architecture.md`,
  `docs/api-contract.md`, UC-04/05/07/08/09/10, and `AGENTS.md`.
- Removed the server-stopped check and Control status request from
  `FetchGitHandler`, `GitSyncHandler`, `CommitRuntimeHandler`,
  `ApplySourceHandler` and `RevertRuntimeHandler`.
- Removed obsolete lifecycle-specific rejection reasons/response mapping in
  the affected Plugins HTTP controllers. Removed unused Control status client
  registration and Control options startup validation from Plugins DI.
- Kept the shared exclusive mutation guard, Git/source validation, Runtime
  Diff selection safety, Baseline persistence, rollback and Production
  restrictions intact. Commit + Push still writes only to Git workspaces,
  pushing selected Runtime data; Apply/Revert overwrite only managed Oxide
  Runtime paths by the documented source of truth.
- Updated `RustServer.Web` Plugins dialogs to stop asking the operator to
  shut down Rust before Fetch/Sync, Commit + Push, Apply or Revert. Kept
  destructive action confirmations and Git safety messages.
- Adjusted integration/unit tests to exercise Fetch, Git Sync, Apply,
  Commit + Push and Revert while the test Rust server is marked running;
  retained the Production-only prohibition on Commit Runtime.

## Current capability

PluginManagement operations execute independently of live Rust process state.
Git state, managed Runtime bytes, Baseline and result reports retain their
existing backend contracts. Oxide/plugin reload status is neither inferred
nor verified; operator chooses when and how to handle the effect of live
file changes.

## Deliberately deferred

- No automatic Rust stop/start/restart, plugin unload/reload orchestration,
  plugin health diagnosis or other runtime side-effect detection.
- Other server-owned destructive operations (e.g. world wipe, consistent
  snapshot restore) retain their own lifecycle constraints outside Plugins.
- No API route, JSON DTO or contract-generation endpoint shapes changed.
  Plugins bootstrap provisioning needs no changes to execute these requests;
  the existing deployment topology may still provide the unused Control URL.

## Validation

- Inspected the five command handlers, specification, related acceptance
  tests, frontend dialog text and Platform Bootstrap integration points.
- Added/updated tests for the live-server scenarios and reviewed all GitHub
  `develop` modifications.
- **Pending local execution:** `dotnet test` and
  `dotnet run --project tools/RustServer.Plugins.ApiContractGenerator -- --verify`
  in `RustServer.Plugins`, plus `npm run check` in `RustServer.Web`.
  The .NET SDK is not available in this connected editing environment;
  local browser verification is also pending.

## Decisions

- Running/stopped is not a Plugins mutation precondition; Git/Runtime
  exclusive mutation remains required.
- Managed Oxide files can be changed live without promising any particular
  behavior from the Oxide framework or individual plugin.
