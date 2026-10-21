# Iteration 048 — Persistent Plugins operation history

## Goal

Preserve detailed RustServer.Plugins mutation reports across service restarts
and expose a compact, cursor-paginated operation history for operators.

## Added and updated

- Plugins stores operation reports in a service-owned SQLite database with WAL,
  schema versioning, indexed stable cursor pagination, and detailed report
  lookup by operation ID.
- `GET /api/operations` returns compact newest-first operation summaries;
  `GET /api/operations/{operationId}` now reads persistent history.
- An operation left in progress by a process stop is marked `interrupted` with
  an `OperationInterrupted` diagnostic on the next service startup.
- Bootstrap supplies the explicit persistent database path
  `PluginManagement__OperationHistory__DatabasePath` under the existing
  service-owned state root. The existing ownership and permissions checks cover
  the database directory.

## Current capability

Operators can review a paged history of completed, failed, and interrupted
Plugins operations after a service restart, then retrieve any selected full
report for diagnosis.

## Decisions

- Operation history remains a Plugins-owned SQLite capability, not a shared
  repository abstraction or a new deployment service.
- Bootstrap preserves the existing state root and never deletes the database
  on a rerun.
