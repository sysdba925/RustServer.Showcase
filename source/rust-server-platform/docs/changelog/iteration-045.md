# Iteration 045 — Plugins Runtime Change notifications and content

## Goal

Expose a read-only Runtime Change API in RustServer.Plugins so an operator UI
can refresh changed managed files promptly and inspect an individual Runtime
Diff entry without confusing current Git HEAD with the applied Baseline.

## Added and updated

- Added `GET /api/runtime/events`, a Server-Sent Events invalidation stream for
  debounced/coalesced changes to managed Runtime paths. It carries no diff,
  path list, sequence, or persistent state.
- Added `GET /api/runtime/diff/content` for a mapped Runtime Diff entry. It
  returns the Baseline and Runtime sides as UTF-8 text where safe, identifies
  missing sides for added/deleted files, and reports binary content without
  constructing presentation HTML.
- Moved the existing Runtime Diff reader into the cohesive
  `PluginManagement/RuntimeChanges` module with the watcher, notifier, and
  content reader. The module continues to use the existing Runtime, Baseline,
  LocalGit, and managed-file mapping capabilities.
- Updated the Plugins behavioral/API documentation and committed generated
  OpenAPI contract for the two public endpoints.

## Current capability

An integrating client can establish an SSE stream, treat `runtimeChanged` as a
prompt to reread `GET /api/runtime/diff`, and lazily retrieve a current diff
entry's exact Baseline-SHA and Runtime content. These read paths remain
available while Rust is running and do not fetch, checkout, mutate Git, mutate
Runtime files, or write Baseline state.

## Deliberately deferred

- No event persistence, replay, change-history endpoint, or cross-process
  notification broker is introduced.
- The Web client integration that consumes this optional SSE stream remains
  separate work.

## Validation

- Focused Plugins API/notifier tests cover SSE delivery, managed-path filtering
  and debounce, exact-Baseline content, binary/missing sides, and invalid or
  unchanged entries.
- The complete Plugins solution build, test suite, and generated OpenAPI
  verification are recorded with the implementation change.

## Decisions

- Filesystem notifications are invalidations only; watcher overflow is also an
  invalidation because a fresh Runtime Diff remains authoritative.
- Runtime Diff content always reads the commit SHA stored in Baseline rather
  than a mutable local or remote branch HEAD.
- Platform Bootstrap needs no change: the service executable, environment
  variables, state root, filesystem layout, permissions, startup behavior, and
  readiness probe are unchanged.
