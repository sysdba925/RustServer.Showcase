# Iteration 060 — Updater StaticWeb registration contract

## Goal

Prepare the Updater registration contract for future static-web deployments
without changing package preparation, privileged activation, or platform
infrastructure.

## Added and updated

- `RustServer.Updater` registrations now have an `ApplicationType` of `DotNet`
  or `StaticWeb`; an omitted legacy `Type` resolves to `DotNet`.
- `DotNet` requires a systemd service and DLL entrypoint. `StaticWeb` requires
  `EntryFile=index.html` and forbids those .NET-only fields.
- Registrations can override the default health path with a validated,
  per-application `HealthPath`; only populated systemd units must be unique.
- The read-only registration API and its OpenAPI documentation now expose the
  application type and type-dependent fields.

## Current capability

The Updater can safely parse, validate, retain, and return the new registration
shape while existing .NET INI registrations remain unchanged and compatible.

## Deliberately deferred

- StaticWeb package-root validation, staging reuse, installation, activation,
  rollback, recovery, and runtime status policies.
- Deployment Executor parser parity and type-specific privileged activation.
- Nginx installation, static serving, and release-identifying readiness.
- Adding a StaticWeb application to the root-owned production registry.

## Validation

- `dotnet build RustServer.Updater.slnx --no-restore`
- `dotnet test RustServer.Updater.slnx --no-restore` (85 passed)
- Parsed the existing legacy platform registry and an in-memory StaticWeb
  registration with `ApplicationRegistrationIniParser`.

## Decisions

- `StaticWeb` remains registration-only until its matching executor and Nginx
  policies are implemented, so it cannot be deployed through a parser-only
  contract change.
