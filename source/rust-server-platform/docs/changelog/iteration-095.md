# Iteration 095 — Fast snapshots no longer require Rust RCON

## Goal

Allow the operator to create a Fast Snapshot by archiving the existing
runtime files without depending on the Rust WebRCON connection. Address the
reported failure `Snapshot creation failed: Rust server RCON command failed`
while preserving the stronger stopped-server safeguard for Consistent and
Restore operations.

## Added and updated

- `RustServer.Snapshots/SnapshotOperationService` now archives files directly
  for Fast requests instead of requiring the `server.save` RCON command.
  Consistent creation and Restore continue to require a stopped Rust server,
  verified with `RustServer.Control`.
- Removed the RCON command client, configuration validation and
  `RustServer.Rcon` package dependency from the Snapshots service.
  Snapshot ZIP creation, metadata, retention, operation polling, paths, and
  restore logic are unchanged.
- Updated the service's Fast semantics in the API operation description,
  source-derived committed OpenAPI/Markdown contract descriptions,
  `docs/snapshot-design.md`, `docs/ui-use-cases.md`, and README.
- Added API tests demonstrating Fast creation while the server is running,
  Consistent rejection while running and creation while stopped, without
  configured RCON secrets.

## Current capability

Fast immediately archives the configured `server/` identity directory and
`oxide/data`, `oxide/config`, `oxide/plugins`, and `oxide/lang`
directories from their **on-disk contents**, including on a running server.
It does not request a Rust save or modify its lifecycle. Existing snapshot
list/create/restore/delete endpoints and DTO shapes remain the same.

## Deliberately deferred

- Fast does not guarantee a transactionally consistent point-in-time capture
  when Rust is running. Unsaved world/plugin state may be absent and files
  modified concurrently may be inconsistent. To capture stable on-disk
  data, stop the server and create a Consistent Snapshot.
- No new RCON fallback, automatic save scheduling, lifecycle orchestration,
  offsite backup, or Web UI interaction was added.
- Other services may continue to use Rust RCON; only Snapshots becomes
  RCON-independent. No production environment secret redistribution was
  included.

## Validation

- Compared the reported error path against the committed Snapshots
  design, code and published API documentation.
- Verified the updated source has no RCON client references, removed
  required RCON configuration from test topology, and updated integration
  scenarios. Source-derived contract text in JSON and Markdown was
  synchronized with `Program.cs` without changing endpoint signatures.
- **Pending local execution:** `dotnet test RustServer.Snapshots.sln` and
  `dotnet run --project tools/RustServer.Snapshots.ApiContractGenerator -- --verify`.
  A .NET SDK is not available in this GitHub-only editing environment.
  Re-run the generator if verification detects drift before publishing.

## Decisions

- Fast is a best-effort filesystem archive and deliberately does **not**
  claim to contain current unsaved runtime state.
- Consistent remains server-stopped and is the recommended option when
  backup completeness is critical.
- The service owns archive files and metadata; RustServer.Control retains
  exclusive server lifecycle ownership.
