# Iteration 025 — Server lifecycle information architecture

## Goal

Separate read-oriented server awareness from operational lifecycle details by
establishing Overview, Server, and World as the current top-level application
areas.

## Added and updated

- RustServer.Web now routes Overview (`/`), Server (`/server`), and World
  (`/world`) from the primary navigation with exact active-route styling.
- Overview now presents a server summary and `Manage server` shortcut, keeps
  Rust/Oxide software updates and known Activity, and excludes lifecycle
  controls and detailed startup milestones.
- Server now owns Runtime lifecycle actions and the detailed startup schema and
  snapshot presentation. Milestone labels and state badges use a constrained
  left-aligned ordered list.
- Startup state is shell-scoped with lifecycle, software, and known operation
  state, preserving one set of guarded requests and pollers while operators
  move between Overview and Server.
- World is a truthful placeholder only; it does not imply current world,
  snapshots, backup, restore, or wipe capabilities.
- UI information-architecture, screen, prototype, and API-mapping documents
  now describe the new boundaries.

## Current capability

Operators can use Overview to assess the current server and update context, use
Server to Start, Stop, Restart, and inspect authoritative startup milestones,
and navigate to a stable World domain boundary without being offered unsupported
actions. While Starting, Overview shows only a source-derived resolved-step
count when an active startup session is available.

## Deliberately deferred

- World data, snapshots, backups, restore, wipe, and all corresponding APIs.
- Operation history, cancellation, live update output, percentages, stages,
  startup history, logs, and inferred milestone progress.

## Validation

- `npm run check` passed in RustServer.Web: lint, typecheck, 115 Vitest tests,
  and production build all succeeded.
- `git diff --check` passed for RustServer.Web and the shared platform
  documentation repository.

## Decisions

- Startup schema labels and snapshot positions remain Control API source data.
  A new `sessionId` replaces the displayed run; readiness and startup polling
  remain separate.
- Overview remains a summary surface. Lifecycle controls, detailed source
  Running/Ready values, and milestone detail belong exclusively to Server.
- The World route is an information-architecture boundary, not a claim that
  any World API exists.
