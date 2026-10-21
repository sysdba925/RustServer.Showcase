# Iteration 061 — Updater StaticWeb package preparation

## Goal

Extend the existing RustServer.Updater preparation pipeline so prebuilt
StaticWeb distributions can be downloaded, verified, and immutably staged with
the same safeguards as DotNet packages, without changing activation or platform
infrastructure.

## Added and updated

- Package-root validation now selects the registration's required file:
  DotNet requires its DLL entrypoint and StaticWeb requires `EntryFile=index.html`.
- Both package types accept the required file at archive root or below one
  top-level directory, then stage the complete selected distribution tree.
- Existing checksum verification, archive safety limits, immutable manifests,
  atomic staging publication, operation tracking, cancellation, and cleanup
  remain shared for both types.

## Current capability

The Updater can prepare a verified prebuilt StaticWeb release from GitHub
Releases using the established asset and checksum convention. It does not build
frontend source or invoke npm on the server.

## Deliberately deferred

- Deployment Executor parser parity, StaticWeb activation, rollback, recovery,
  and runtime-status policies.
- Nginx installation, static serving, release-identifying readiness, and live
  StaticWeb registration or deployment.

## Validation

- `git diff --check` passed for the Updater implementation and documentation.
- `dotnet build RustServer.Updater.slnx --no-restore` passed with zero warnings
  and zero errors.
- `dotnet test RustServer.Updater.slnx --no-build --no-restore` passed: 85
  tests, zero failures. No tests were added or changed for this iteration.

## Decisions

- StaticWeb remains a prebuilt payload. Its type affects only the required
  package entry file at this stage; it does not create a separate preparation
  pipeline.
