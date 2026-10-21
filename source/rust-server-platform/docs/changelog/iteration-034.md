# Iteration 034 — Align Plugins UI contract with published backend workflows

## Goal

Align the Web and platform UI documentation with the current
`RustServer.Plugins` public contract and add its initial Environment-aware
deployment UI, without treating HTTP endpoints as a screen map.

## Added and updated

- Documented Plugins as a planned, currently-backed product area and separated
  its Git-backed source, deployment, package, runtime-change, and Environment
  responsibilities from Server lifecycle and World operations.
- Added the published Plugins operations to the UI-to-API mapping, grouped by
  operator purpose across the seven Plugins use cases.
- Added independent Plugins Environment, deployed Source, runtime-change, and
  request-state semantics, including the fail-closed unknown-source state.
- Updated RustServer.Web contract guidance and configuration documentation for
  the Plugins backend.
- Updated the Plugins overview to read and display the published logical
  Environment instead of reporting that the capability is unavailable.
- Added exact Environment DTOs and `GET`/`PUT /api/environment` clients.
- Replaced the Deploy page's parallel Staging/Production-target model with one
  current Environment that determines which source deployments are available.

## Current capability

The documentation now represents the published Plugins Environment, update
package, Development deployment, Release, runtime-change, and Production
Release workflows. It makes clear that an Environment change is not a deploy or
Rust lifecycle action, that Production requires persistent visual context, and
that the UI reads current Environment and contract-established Source without
inferring either from a local selection.

## Deliberately deferred

- There is no invented generic Staging deployment-status API. The UI displays
  Source as unknown or unavailable when a published baseline response cannot
  establish it.
- Final page, navigation, tab, card, component-tree, state-library, and diff
  presentation decisions remain outside this documentation iteration.
- Plugin version discovery and catalog scraping remain unsupported because the
  published backend contract does not provide them.

## Validation

- `git diff --check` completed successfully in RustServer.Web and
  rust-server-platform.
- `npm run check` completed successfully in RustServer.Web: typecheck, lint,
  21 test files / 173 tests, and production build. Lint retains two
  pre-existing `react(set-state-in-effect)` warnings in
  `usePluginsResources.ts`.

## Decisions

- Backend OpenAPI is the source of truth for capability, backend use cases for
  operator workflow, and the platform UI contract for Web representation;
  frontend documentation cannot override backend contracts.
- `Staging` and `Production` are logical Plugins Environment modes, while
  `Development` and `Release` are independent deployed-source states.
