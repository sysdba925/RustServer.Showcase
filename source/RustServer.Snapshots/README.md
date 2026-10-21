# RustServer.Snapshots

`RustServer.Snapshots` creates, lists, restores, and deletes local filesystem
snapshots of Rust Dedicated Server runtime state. Create and restore execute as
in-process asynchronous operations.

The service owns snapshot archives only. `RustServer.Control` owns Rust server
start/stop/restart; this service never changes lifecycle itself.

## Configuration

Configure these absolute paths through the `Snapshots` section. Source and
storage paths must be distinct and must not overlap.

| Setting | Required | Meaning |
| --- | --- | --- |
| `ServerIdentityPath` | Yes | The complete `server/<identity>/` directory to capture and restore. |
| `OxideDataPath` | Yes | The `oxide/data/` directory. |
| `OxideConfigPath` | Yes | The `oxide/config/` directory. |
| `OxidePluginsPath` | Yes | The `oxide/plugins/` directory. |
| `OxideLangPath` | Yes | The `oxide/lang/` directory. |
| `StoragePath` | Yes | Local directory holding snapshot archives and metadata. |
| `RetentionCount` | No | Number of newest snapshots to retain; defaults to `10` and must be at least one. |
| `MinimumFreeSpaceBytes` | No | Minimum free bytes required before a create operation; defaults to 512 MiB. Set to `0` only when deliberately disabling the guard. |

`RUSTSERVER_CONTROL_URL` is required for checking server state when
creating Consistent snapshots and during Restore. Fast snapshots directly
archive current on-disk files without RCON or `server.save`; if Rust is
running, unsaved changes may be absent and files may change mid-copy.

## Storage format

Each snapshot uses a stable opaque ID and produces two files in `StoragePath`:

- `<id>.zip` — compressed runtime state archive;
- `<id>.json` — metadata with `id`, `createdAt`, `type`, and `sizeBytes`.

ZIP is used instead of `tar.zst` because it is available in the .NET runtime
without adding a compression dependency. The archive uses logical `server/` and
`oxide/...` roots, never absolute paths.

The configured directories are a strict whitelist: their ordinary contents are
captured verbatim, without exclusions based on file or directory names. The
only skipped entries are filesystem reparse points, such as symbolic links or
junctions, so traversal cannot escape a configured source tree.

## Running API

Set the required paths and use the platform topology loader, then run:

    ../rust-server-platform/scripts/dev/with-topology.sh dotnet run --no-launch-profile --project src/RustServer.Snapshots.Api

The service derives its listening port from `RUSTSERVER_SNAPSHOTS_URL` and
its IPv4 listening interface from `RUSTSERVER_SNAPSHOTS_BIND_HOST`. The
topology URL remains the connection address and source of truth for the port;
the service fails clearly if a required topology value is missing or invalid.

## Swagger UI

In the Development environment, open
`$RUSTSERVER_SNAPSHOTS_URL/swagger`. The live OpenAPI document is always
available at `$RUSTSERVER_SNAPSHOTS_URL/openapi.json`.

## Generating OpenAPI

Generate the committed contract artifacts from the API:

    dotnet run --project tools/RustServer.Snapshots.ApiContractGenerator

This updates `openapi.json` and `docs/api-contract.md`.

## Verifying OpenAPI contract

Verify without writing files:

    dotnet run --project tools/RustServer.Snapshots.ApiContractGenerator -- --verify
