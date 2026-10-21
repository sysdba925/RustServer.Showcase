# Iteration 026 — Control server configuration and explicit wipe

## Goal

Add a safe Control-owned desired/effective game-server configuration boundary
and an explicit stopped-server wipe operation without coupling wipe to update,
snapshot, or lifecycle orchestration.

## Added and updated

- RustServer.Control now exposes typed desired/effective configuration for
  hostname, description, player limit, tick rate, save interval, seed, and
  world size. The desired/effective state is persisted in the local,
  Git-ignored Compose configuration directory.
- Control supplies desired settings to the Rust Compose launch environment;
  the Rust Compose script applies description, tick rate, and save interval in
  addition to the existing hostname, player, seed, and world-size settings.
- Control now queues an explicit `Wipe` operation through the existing
  operation store and mutation lock. Rust must be stopped; wipe never starts,
  restarts, snapshots, restores, or updates the server.
- The initial wipe plan independently keeps or deletes recognized world save
  files and recognized player-blueprint stores. Unknown Rust data, plugin
  binaries, plugin configuration, and unknown `oxide/data` content remain
  untouched.
- Platform runbook and UI API mapping now distinguish the backend wipe and
  configuration capabilities from the still-placeholder World screen.

## Validation

- RustServer.Control solution build and complete test suite passed.
- Generated Control OpenAPI and Markdown contract were regenerated and
  verified.
- Updated Rust Compose and Control bootstrap scripts passed `bash -n`; both
  Control and platform diffs passed `git diff --check`.

## Decisions

- Wipe target names are semantic API values, not client-supplied paths. The
  filesystem resolver permits only the configured Rust data root and identity
  directory, and does not recursively delete directories.
- Plugin data remains keep-by-default. Future plugin-specific delete or
  transform behavior must be registered in Control through a trusted handler.
