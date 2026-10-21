# Iteration 062 — StaticWeb deployment executor

## Goal

Extend the existing root-owned Deployment Executor with the Stage 03
type-specific activation and recovery policies while preserving the shared
transaction, manifest, lock, and privilege-boundary mechanisms.

## Added and updated

- The executor independently parses the Stage 01 registration contract:
  legacy `Type` defaults to `DotNet`; `StaticWeb` requires `EntryFile=index.html`
  and forbids service and DLL fields; per-application `HealthPath` is supported.
- DotNet keeps systemd stop/start/active checks and HTTP readiness.
- StaticWeb verifies an ordinary `index.html`, installs into the existing
  immutable releases layout, atomically switches `current`, and performs no
  systemd or Nginx lifecycle operation.
- StaticWeb readiness and Updater runtime status require HTTP 200 plus exactly
  one `X-RustServer-Release` response header matching the resolved current
  release tag. This prevents a generic/default HTTP response from proving a
  release active.
- The existing shared rollback and boot-time recovery paths restore the prior
  `current` link; only DotNet performs service actions during restoration.

## Current capability

The executor remains the sole root-side deployment mechanism. It validates the
full staged manifest before copying and after copying, retains durable
transactions, and receives only an operation UUID from the non-root Updater.

## Deliberately deferred

Stage 04 must configure the local Nginx readiness route and
`X-RustServer-Release` response header. No StaticWeb registration is added to
the live INI and no Nginx installation, routing, reload, or frontend release
pipeline is included here. Until Stage 04, a StaticWeb activation intentionally
fails readiness and rolls back safely.

## Validation

- `bash -n deploy/bootstrap/assets/rustserver-deployment-executor`
- `dotnet build RustServer.Updater.slnx --no-restore`
