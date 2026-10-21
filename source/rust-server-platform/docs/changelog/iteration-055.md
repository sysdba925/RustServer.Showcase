# Iteration 055 — Plugins operation-history 200-item pages

## Goal

Extend the published RustServer.Plugins compact operation-history page limit so
operators and API clients can request up to 200 summaries while preserving the
existing default and stable cursor workflow.

## Added and updated

- `GET /api/operations` now accepts `limit` values from 1 through 200.
- Preserved the default limit of 20, opaque cursor continuation, ordering, and
  existing invalid-query error contract.
- Added generated OpenAPI metadata for the minimum, maximum, and default
  limit, and updated the behavioral API contract and tests.

## Current capability

API clients can request compact persistent operation-history pages at the
approved sizes 10, 50, or 200 without splitting a requested page. A cursor
continues after the existing `(startedAt, operationId)` boundary exactly as
before.

## Deliberately deferred

- Operation-history presentation and client pagination remain owned by the
  separate RustServer.Web implementation work.

## Validation

- RustServer.Plugins Release build completed with zero warnings and errors.
- RustServer.Plugins Release test suite passed: 89 tests.
- RustServer.Plugins OpenAPI contract verification passed with
  `dotnet run --project tools/RustServer.Plugins.ApiContractGenerator --configuration Release --no-build --no-restore -- --verify`.

## Decisions

- The default, lower bound, and upper bound are shared by the controller,
  SQLite history store, and generated OpenAPI metadata to prevent contract
  drift.
- Bootstrap requires no change: this contract extension introduces no service,
  topology, configuration, filesystem, permission, or readiness change.
