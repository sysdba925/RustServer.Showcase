# Snapshot service design

## Responsibility and boundaries

`RustServer.Snapshots` owns local archive creation, listing, restoration,
deletion, retention, and the in-process status of create and restore
operations. It captures runtime state only.

| Component | Boundary |
| --- | --- |
| `RustServer.Control` | Owns lifecycle. Snapshot requests query `GET /api/server/status` and require a stopped server for Consistent create and Restore. Snapshots never sends start, stop, or restart commands. |
| `RustServer.Versions` | Not called by this implementation. Version metadata is not captured yet. |
| `RustServer.Plugins` | Owns plugin management. Snapshots preserve the configured Oxide plugin directory but do not manage plugins. |
| `RustServer.Web` | Displays snapshot and operation state through the public HTTP API; it must not replace backend server-state checks. |

## Configured paths

The `Snapshots` configuration section defines six required absolute paths:

- `ServerIdentityPath` maps to the archive root `server/`;
- `OxideDataPath` maps to `oxide/data/`;
- `OxideConfigPath` maps to `oxide/config/`;
- `OxidePluginsPath` maps to `oxide/plugins/`;
- `OxideLangPath` maps to `oxide/lang/`;
- `StoragePath` contains snapshot files.

Paths must be distinct and non-overlapping. Creation verifies that every source
directory exists. Restore creates destination directories when needed.

`RetentionCount` defaults to 10 and removes the oldest snapshots after a
successful create. `MinimumFreeSpaceBytes` defaults to 512 MiB and is checked
before a create operation is accepted; it is a deliberately simple protection
against an obviously full storage volume, not a compressed-size estimate.

`RustServerControlApi:BaseUrl` identifies the Control API. 

## Snapshot contents and storage

A snapshot is a built-in .NET ZIP archive plus a JSON metadata sidecar:

    <StoragePath>/<id>.zip
    <StoragePath>/<id>.json

Metadata contains `id`, `createdAt`, `type`, and `sizeBytes`. The stable ID is
generated independently of time; `createdAt` is the UI-facing identifier.

The archive contains:

    server/                  contents of ServerIdentityPath
    oxide/data/              contents of OxideDataPath
    oxide/config/            contents of OxideConfigPath
    oxide/plugins/           contents of OxidePluginsPath
    oxide/lang/              contents of OxideLangPath

ZIP was selected instead of `tar.zst` because it is available through the .NET
runtime with no additional compression dependency. Archive paths are logical
and never contain configured absolute paths.

The five configured source directories are the complete whitelist. Every
ordinary file and directory below them is archived verbatim, including names
such as `cache`, `logs`, `temp`, `.git`, and `.log` files; plugins may use any
of these as persistent state. The only traversal exclusion is a filesystem
reparse point (for example, a symbolic link or junction), which prevents the
archive operation from escaping a configured source tree. Rust/Oxide binaries
are not among the configured source directories, so they are not archived.

## Snapshot types

### Fast

A Fast snapshot may run while Rust is running:

    create ZIP archive directly from configured runtime directories
    → write snapshot metadata

Fast requires no RCON, and may run while Rust is running or stopped. It
captures the files already present on disk at archive time; Rust and plugins
may have unsaved in-memory changes or modify files during the copy. It is
therefore **best-effort, not an atomic or guaranteed up-to-date backup**.
Filesystem or archive failures mark the asynchronous operation as failed.

### Consistent

A Consistent snapshot requires Rust to be stopped. On request and again in the
worker, the service queries `RustServer.Control`. When the server is running,
the request returns `409 ServerMustBeStopped`, or an already accepted operation
fails if the state changed before execution. The Snapshot service never
controls lifecycle itself.

## Operations

Create and Restore enqueue work in one in-process, single-reader background
worker. They return:

    202 Accepted
    { operationId, status: "pending" }

`GET /api/operations/{operationId}` exposes `pending`, `running`,
`succeeded`, or `failed`, with timestamps and a safe failure message. Operation
state is intentionally in-memory and is lost when the service restarts; no
distributed queue or database is used.

A pending or running Restore prevents deletion of its target snapshot. While a
Create is pending or running, deletion is conservatively blocked so a newly
written archive cannot be removed during its finalization. Both cases return
`409 SnapshotOperationInProgress`.

## Restore

Restore is destructive and only runs while Control reports Rust as stopped. It:

1. verifies the archive exists;
2. extracts it to a temporary directory within snapshot storage;
3. rejects archive entries outside the known `server/` and `oxide/...` roots,
   preventing path traversal;
4. verifies every required root is present;
5. clears the configured destination directories and copies staged content.

There is no automatic Stop/Start, rollback, binary restore, or version
rollback. A filesystem failure during destructive replacement can leave a
partially restored state and is reported as a failed operation.

## Deferred scope

This implementation intentionally excludes scheduled restart orchestration,
player warnings, SteamCMD, Oxide or Rust download/rollback, deduplication,
incremental or chunk storage, SQLite, remote/cloud storage, encryption, and
off-site replication.
