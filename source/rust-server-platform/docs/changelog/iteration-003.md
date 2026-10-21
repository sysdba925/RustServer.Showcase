# Iteration 003 — Local Rust WebRCON

## Goal

Enable secure, localhost-only Rust WebRCON access for RustServer.Control without changing the Docker-owned server lifecycle.

## Changed files

- `docker/rust-server/.env.example`
- `docker/rust-server/compose.yml`
- `docker/rust-server/scripts/start-rust.sh`
- `scripts/dev/run-control.sh`
- `docs/changelog/iteration-003.md`

## Decisions

- Rust Dedicated Server starts with `+rcon.port`, `+rcon.password`, and `+rcon.web 1`; `-batchmode` remains unchanged.
- RCON is TCP and Docker publishes it only as `127.0.0.1:${RUST_SERVER_RCON_PORT}`. It is not publicly exposed.
- `RUST_SERVER_RCON_PASSWORD` is required at startup, remains in the local Git-ignored `.env`, and is never committed or printed by scripts.
- `run-control.sh` preserves an explicitly supplied `RustServerRcon__Password` and can otherwise forward `RUST_SERVER_RCON_PASSWORD` from the caller environment without logging it.
- Docker remains the source of truth for container lifecycle. RustServer.Control uses WebRCON only to request graceful shutdown.

## Current capability

With matching local RCON configuration in both repositories, RustServer.Control can issue a WebRCON command to the running Rust server over the host loopback interface. The platform keeps server data in the host-mounted `/srv/rust/server` directory.

## Out of scope

- Public RCON access
- Persistent RCON connections, live console streaming, and event subscriptions
- Oxide/uMod, plugins, and plugin deployment
- Rust server readiness checks, scheduled restarts, backups, and monitoring
