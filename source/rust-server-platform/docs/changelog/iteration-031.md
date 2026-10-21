# Iteration 031 — Plugins update-package workflow

## Goal

Provide the UC-04 operator workflow in RustServer.Web for uploading, reviewing,
and committing a server-owned update package without treating a package as a
single plugin source file or triggering deployment.

## Added and updated

- Added Plugins Updates entry and package routes for upload and reloadable
  package inspection.
- Added multipart package upload, authoritative package/Incoming Changes reads,
  text-diff viewing, managed changed-path selection, and explicit commit review.
- Commit results present commits from affected repositories as one
  UpdatePackage operation. Successful commits refresh package state and do not
  deploy, release, or restart Rust.
- Added update-package API contract tests and route workflow tests, including
  stale comparison handling, package-not-found, selection constraints, and
  multipart upload behavior.
- Scoped Plugins CSS element styles beneath feature classes to avoid global
  styling effects on other product surfaces.

## Current capability

An operator can upload a supported package, reopen it by its server-owned ID,
inspect classified incoming paths, compare textual files with their published
workspace-head token, select only changed Managed paths, supply a commit
message, and finalize one UpdatePackage that may yield commits in both managed
repositories.

## Deliberately deferred

- Runtime Changes, runtime diff, Import, Discard, wipe integration, catalog,
  enable/disable, arbitrary configuration editing, deployment after commit,
  automatic restart, and package history remain out of scope.
- Current Environment and Source cannot be read or changed because the
  published Control and Plugins contracts do not expose that capability; Web
  relies on backend enforcement and does not infer local instance state.

## Validation

- `npm run typecheck` completed successfully.
- `npm run test` completed successfully: 20 files and 158 tests passed.
- `npm run lint` completed successfully with two pre-existing
  `react(set-state-in-effect)` warnings in `usePluginsResources.ts`.
- `npm run build` completed successfully, with Vite's existing large-chunk
  advisory after adding the diff viewer.
- Local Vite fallback returned HTTP 200 for `/plugins`, `/plugins/deploy`,
  `/plugins/releases`, `/plugins/updates`, and
  `/plugins/updates/known-package-id`.

## Decisions

- Update-package DTOs and requests mirror the published RustServer.Plugins
  OpenAPI. Workspace HEAD tokens from Incoming Changes are passed directly to
  diff and commit; a 409 clears transient selection/diff and refreshes the
  comparison without retrying a mutation.
- `react-diff-viewer-continued` supplies the text-diff presentation rather than
  a custom diff engine.
