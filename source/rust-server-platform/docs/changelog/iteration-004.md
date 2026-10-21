# Iteration 004 — Asynchronous Rust update operations

## Goal

Expose Rust Dedicated Server updates through `RustServer.Control` as tracked background operations, without blocking the HTTP request for SteamCMD execution.

## Changed components

- `RustServer.Control` operation model, in-memory operation store, queue, and background worker
- `RustServer.Control` server lifecycle coordination and update API
- `RustServer.Control` API and concurrency test coverage
- `docs/changelog/iteration-004.md`

## Decisions

- `POST /api/server/update` creates a `RustUpdate` operation and returns `202 Accepted` with an operation id, status, and `Location` header.
- `GET /api/operations/{id}` returns the current operation state; unknown ids return `404`.
- Operations are held only in process memory. A restart of `RustServer.Control` discards operation history.
- A single-reader in-memory `Channel` executes accepted updates one at a time. The worker uses application-shutdown cancellation, not the HTTP request cancellation token.
- Before invoking SteamCMD, the worker acquires the shared server mutation lock, checks the server state through the lifecycle abstraction, and retains that lock until SteamCMD exits. A running server causes the operation to fail without invoking SteamCMD.
- Lifecycle mutations do not queue behind a pending or running update: `POST /api/server/start`, `/stop`, and `/restart` return `409 Conflict`. The control implementation repeats this check inside the mutation lock to close the check-to-action race.
- A second update while a `RustUpdate` is pending or running returns `409 Conflict` and is not queued.
- `GET /api/server/status` remains available during an update.
- The REST contract exposes only safe operation metadata and concise errors. SteamCMD stdout/stderr are not exposed through the API.

## Current capability

RustServer.Control can accept a Rust update immediately, expose its `Pending`, `Running`, `Succeeded`, or `Failed` state, retain a SteamCMD exit code, and prevent incompatible lifecycle changes while an update is active.

The implementation was verified with `dotnet build` (zero warnings and errors) and `dotnet test` (65 passed tests).

## Out of scope

- Persistent operation history, databases, Redis, or distributed queues
- Operation cancellation, live output streaming, SSE, or SignalR
- Percentage progress or operation stages
- Oxide/uMod, plugin deployment, configuration deployment, and automatic server restart after an update
- Multiple Rust server instances
