# Iteration 016 — RustServer.Web Read-only Overview API integration

## Goal

Replace the deterministic Overview runtime input with independent read-only
Control and Versions API loading, while keeping the screen free of lifecycle,
update, and operation-observation mutations.

## Added and updated

- `shared/api` now owns contract-aligned Control and Versions SOURCE types, a
  native-fetch JSON GET helper, and typed functions for server status, Rust
  versions, and Oxide versions.
- Vite development proxies `/control-api` and `/versions-api` to the local
  Control and Versions services. Environment defaults use those prefixes while
  retaining explicit base-URL overrides.
- `OverviewPage` starts the three read-only requests independently on mount and
  owns a local `ResourceState` for every response. A failed resource remains
  local and does not blank successful sibling regions.
- Overview request cleanup aborts its shared `AbortController`; cancelled
  results do not become unavailable states. Activity remains “No known
  operation” until a future accepted update supplies an operation ID.
- `overviewScenarios` remain test and development fixtures, but no longer drive
  production Overview rendering.
- Operation presentation handles source type/status values defensively:
  unknown types are not labelled as Oxide, and unknown statuses remain explicit
  and block actions rather than being treated as terminal.
- Focused API and Overview integration tests cover GET behavior, local loading,
  successful independent resources, partial failure, and abort cleanup.

## Current capability

The frontend route at `/` loads server status, Rust versions, and Oxide versions
once through the typed shared API boundary. Every region independently renders
Loading, Available, or Unavailable state. Development requests use the Vite
same-origin proxy; no runtime operation is known until a future update request
returns an operation ID.

## Deliberately deferred

- Lifecycle and Rust/Oxide update mutations, action buttons, operation lookup
  and polling, operation history, progress, stages, cancellation, and live
  output.
- Retry controls, manual or periodic refresh, React Context, global state,
  reducers, React Query, and SWR.
- Aggregate backend-health presentation, fabricated server metadata, uptime,
  start time, and operation duration.

## Validation

- `git diff --check` passed for the shared API, defensive operation, Overview
  integration, and changelog changes.
- `npm run check` could not be rerun because Node.js and npm were unavailable in
  the execution environment; it remains required before integration.

## Decisions

- `shared/api` owns HTTP access and API SOURCE types; `OverviewPage` owns
  independent request state, while child panels remain presentational.
- Development uses stable same-origin proxy prefixes rather than exposing
  backend VM addresses to the browser.
- Deterministic mocks remain fixtures, not a production Overview data source.
- An unknown operation status is treated conservatively until it is understood;
  Maintenance requires a documented active Rust or Oxide update.
