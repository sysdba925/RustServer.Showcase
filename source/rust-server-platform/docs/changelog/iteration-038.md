# Iteration 038 — Preview managed Source changes

## Goal

Add a read-only RustServer.Plugins preview so an operator can inspect the
exact managed Runtime changes before confirming Git-to-server Apply.

## Added and updated

- Added `POST /api/source/preview` with one Target Source request shape for
  Development and Release.
- Preview resolves only already-fetched `origin/develop` or
  `origin/release/<version>` refs for both repositories, reports target SHAs,
  current local Source, Baseline, and binary-safe added/modified/deleted
  managed-file changes.
- Incomplete Releases are rejected with the missing repository rather than
  being presented as applicable.

## Current capability

An operator may preview a Development or complete Release Source while the
Rust server is running. The response is a snapshot of known Git state; Apply
will fetch and validate again before changing a workspace or Runtime files.

## Deliberately deferred

- Apply Source remains the next implementation stage; Preview never changes
  Git workspaces, Runtime files, Baseline, Environment, or operation history.
- No Release rollback endpoint or navigation workflow was added.

## Validation

- RustServer.Plugins Release build passed with warnings as errors.
- RustServer.Plugins tests passed: 26 total, including two new Source Preview
  integration tests for binary-safe added/modified/deleted output, read-only
  behavior while the server is running, and incomplete Release rejection.

## Decisions

- Platform topology and Bootstrap remain unchanged: Preview adds no URL, port,
  bind host, configuration key, filesystem layout, or deployment dependency.
- Source Preview is a dedicated read component; it compares Target Git Source
  directly with Runtime Files and does not repurpose Runtime Diff.
