# Iteration 047 — Plugins selective Runtime Revert

## Goal

Allow an operator to restore only chosen managed Runtime Diff entries from the
persisted Plugins Baseline while retaining the existing full Runtime Revert.

## Added and updated

- `POST /api/runtime/revert` now accepts an optional `changes` body with the
  same repository/path selection format as Runtime Commit.
- An absent body remains backward-compatible Revert all; an explicitly empty,
  malformed, unsafe, duplicate, unmanaged, or stale selection returns
  `InvalidRuntimeSelection` and never becomes Revert all.
- Selected added, modified, and deleted Runtime files are restored exclusively
  from exact Baseline Git objects. Unselected Runtime changes are preserved.
- Updated the Plugins behavioral/API documentation and committed generated
  OpenAPI contract.

## Current capability

With the Rust server stopped, an operator can either restore the full managed
Runtime to its Baseline or restore one or more current Runtime Diff entries.
Both operations retain detailed reporting and rollback; neither changes the
Git workspace, Baseline, or `appliedAt`.

## Validation

- RustServer.Plugins Release build passed with warnings treated as errors.
- The full RustServer.Plugins Release suite passed: 74 tests, including full
  Revert compatibility, selective added/modified/deleted restore, multiple
  files, invalid selections, unselected-file preservation, and selective
  rollback.
- The Plugins OpenAPI contract generator verification completed successfully.

## Decisions

- Platform Bootstrap requires no change: this is an in-service HTTP behavior
  extension with no new executable path, configuration, filesystem layout,
  permission, systemd unit, startup validation, or readiness dependency.
