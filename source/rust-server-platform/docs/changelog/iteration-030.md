# Iteration 030 — Plugins release and deployment surface

## Goal

Provide the first usable RustServer.Web Plugins vertical slice for browsing and
creating immutable releases, and explicitly deploying Development or Releases
to their published Staging and Production targets.

## Added and updated

- Added `/plugins`, `/plugins/deploy`, and `/plugins/releases`, plus Plugins
  navigation and a dedicated feature/API boundary.
- Added release catalog loading, release inspection, and explicit Create
  Release behavior that requests a catalog refresh without deploying anything.
- Added explicit reviewed Staging Development, Staging Release, and Production
  Release deployment flows. Production requires retyping the selected Release
  ID, and a numerically older current Production release is presented as a
  rollback.
- Plugins overview composes the existing Control lifecycle status as advisory
  context and reads the published Production deployment record. Each resource
  keeps independent loading and error states.
- Added a conspicuous Production target treatment and operator-readable HTTP
  conflict/forbidden/not-found errors.

## Current capability

Operators can inspect releases and their complete commit SHAs, create a new
immutable release, deploy Development to the explicit Staging target, deploy a
selected immutable release to Staging or Production, and redeploy an older
Production release as a confirmed rollback.

## Deliberately deferred

- The published Plugins API has no environment read or mutation operation, so
  Web cannot establish a persistent current instance Environment, change it,
  or truthfully condition all Plugins routes on it. The UI uses explicit target
  areas instead and reports unavailable instance context.
- The published API has no Staging deployment read operation, so Staging's
  current baseline is not reconstructed after reload.
- Incoming Changes, Runtime Changes, Import, Discard, update-package upload,
  operation progress/history, lifecycle orchestration, and plugin catalog work
  remain outside this slice.

## Validation

- Added API-client and route/operator-workflow tests for catalog operations,
  explicit deployment confirmation, Production rollback wording, and HTTP 409
  presentation.
- `npm`/`node` were unavailable on the execution host PATH, so the frontend
  test, lint, typecheck, and build commands remain to be run in a Node-enabled
  environment.
- `git diff --check` completed with no whitespace errors.

## Decisions

- Plugins keeps feature-local resource and mutation state; it does not extend
  `ControlPlaneProvider`.
- `RustServer.Plugins` retains deployment and release ownership; Control's
  lifecycle state is display-only in Plugins.
- No transient mutation result or local storage is treated as a deployment
  baseline after reload.
