# Snapshot UI use cases

This document describes the data flow between `RustServer.Web` and the working
`RustServer.Snapshots` API. It does not prescribe page layout, button styling,
or colours.

## Snapshot list

The UI requests `GET /api/snapshots` and receives snapshots ordered newest
first. For each item it displays:

- `createdAt` as the primary human-readable label;
- `type` (Fast or Consistent);
- `sizeBytes`;
- restore and delete actions.

The opaque `id`, not the displayed creation time, is used for all follow-up
requests. An empty `200 OK` array means no snapshots exist.

## Create Fast snapshot

The operator requests:

    POST /api/snapshots
    { "type": "fast" }

The API responds with `202 Accepted` and an `operationId`. The UI polls
`GET /api/operations/{operationId}` until the status is `succeeded` or
`failed`, then refreshes `GET /api/snapshots` on success.

Fast creation directly archives files present on disk and **does not call
RCON or request `server.save`**. The UI should explain that in-memory changes
not yet saved by Rust/plugins might be missing and that concurrent writes
are not atomic. A failed archive operation exposes an error message through
the operation endpoint.

## Create Consistent snapshot

The UI requests the same endpoint with `{ "type": "consistent" }`. A
Consistent snapshot requires a stopped Rust server. The UI may guide the
operator to a Control-owned lifecycle flow, but it does not stop the server
through the Snapshot API.

If the server is running, the backend returns `409 ServerMustBeStopped`. The
UI surfaces this result and must not imply that a client-side preflight check
is sufficient. After `202 Accepted`, it polls the operation as with Fast
create.

## Restore

Restore is destructive. Before submitting it, the UI clearly identifies the
selected snapshot by its creation time, type, and size, and warns that runtime
state will be replaced.

    POST /api/snapshots/{id}/restore
    → 202 Accepted with operationId
    → GET /api/operations/{operationId}
    → refresh server and snapshot state after succeeded

The backend requires Rust to be stopped and returns `409 ServerMustBeStopped`
when it is running. A missing archive returns `404 Not Found`. The UI surfaces
either error, and only considers restore complete after the operation reaches
`succeeded`.

## Delete

The UI sends `DELETE /api/snapshots/{id}`. On `204 No Content` it refreshes
the list. A `404 Not Found` means the item no longer exists and the list should
be refreshed.

When snapshot creation is pending/running or a restore of that snapshot is
pending/running, Delete returns `409 SnapshotOperationInProgress`. The UI
presents the conflict and leaves the snapshot visible rather than removing it
locally.

## Operation state

Operations use the same simple terminology as `RustServer.Control`:
`pending`, `running`, `succeeded`, and `failed`. State is in-process, so an
unknown operation ID (including after a service restart) returns `404 Not
Found`. The UI should retain enough local context to explain that state is no
longer available, then refresh snapshots/server state instead of treating the
operation as successful.
