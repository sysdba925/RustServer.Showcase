# Iteration 085 — Startup polling while the Rust container appears

## Goal

Make the Server page continue observing startup after Start, even when Docker
has not produced a startup session on the first request.

## Added and updated

- Web polls the startup endpoint while Rust is Starting, even when
  `sessionId` is initially null.
- Startup observation also runs while a Start or Restart HTTP request is
  still pending.
- Explicit startup snapshot refreshes re-arm polling rather than
  leaving a previously cancelled poll dormant.
- Added Web regression tests for an initially empty startup snapshot
  and for refresh while Start is pending.

## Current capability

A late Docker container/startup session can now appear in Server → Startup
without manually reloading the page. Polling stops when startup completes.

## Deliberately deferred

- The Docker/runtime cause of the observed failed or delayed Rust start
  remains unverified. Container status and logs must be inspected on the VM.
- Startup tracking still reads the current running container; it does
  not expose logs or exit reasons from previously stopped containers.
- A new Web Release must be published and deployed before the VM uses this fix.

## Validation

- Reviewed the Web ControlPlaneProvider polling paths and the Control
  Docker Compose startup tracking implementation.
- Added two focused Web regression tests.
- GitHub source changes committed; npm tests and live VM checks
  have not been run in this session.
