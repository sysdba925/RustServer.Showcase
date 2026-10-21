# Iteration 069 — Migrate Rust server settings to persistent configuration

## Goal

Make `/etc/rustserver/rust-server.env` the sole active Rust Dedicated Server
settings file while preserving existing development values safely.

## Added and updated

- Added a non-destructive Bootstrap migration from
  `docker/rust-server/.env` to `/etc/rustserver/rust-server.env`.
- Made the migration create-only, validated, and idempotent; an existing
  canonical file is authoritative and the legacy file remains a recoverable
  backup.
- Switched direct development Bootstrap consumers and the Control development
  launcher to canonical Rust settings.
- Made Rust and SteamCMD Compose validation resolve the same canonical env
  file without starting containers.

## Current capability

Operators can edit `/etc/rustserver/rust-server.env` to configure Rust
Dedicated Server settings. Bootstrap preserves valid administrator-managed
settings on reruns without consulting a retained legacy configuration.

## Stage 2 — RCON password migration

- Added non-destructive migration from `docker/rust-server/.rcon-password` to
  `/etc/rustserver/rcon-password`. The canonical password is authoritative;
  Bootstrap only copies the legacy value when the canonical file is absent.
- Switched Rust Docker Compose, the Control development launcher, Rcon gRPC,
  and Snapshots Bootstrap to the canonical secret.
- The canonical raw secret is owned by the workspace owner with mode
  `0600`. Rcon and Snapshots read it at process startup, so generated systemd
  unit files no longer permanently contain the password value.

## Deliberately deferred

- Control JSON-state relocation, installed runtime assets, and other
  configuration migrations.
- Application-code changes in RustServer.Control, RustServer.Snapshots,
  RustServer.Updater, RustServer.Rcon, and RustServer.Auth.

## Validation

- Added focused migration tests for creation, preservation, migration, and
  idempotency.
- Added focused Rust and SteamCMD Compose rendering tests using an explicit
  canonical env file and no container start.

## Decisions

- The checkout-local Rust `.env` is retained only as a recoverable migration
  backup; it is no longer an active configuration source.
- `/srv/rust/server` remains the template default for `RUST_SERVER_PATH`.
