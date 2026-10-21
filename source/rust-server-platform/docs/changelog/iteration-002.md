# Iteration 002 — Rust Dedicated Server runner

## Goal

Provide a Docker Compose runtime for a Rust Dedicated Server already installed on the Linux host.

## Created files

- `docker/rust-server/compose.yml`
- `docker/rust-server/.env.example`
- `docker/rust-server/.gitignore`
- `docker/rust-server/scripts/start-rust.sh`

## Decisions

- SteamCMD remains an update-only Compose utility. The running server has its own Compose project.
- The runner uses the existing `cm2network/steamcmd` Linux image because it supplies the runtime dependencies required by Rust Dedicated Server; it does not copy server files into an image.
- The host directory selected by `RUST_SERVER_PATH` is bind-mounted to `/server`. Rust executable files, saves, configuration, and state therefore survive container removal and recreation.
- The launch script runs `RustDedicated` with `exec`, making it the container's main process.
- Runtime launch settings are environment variables in `.env`, not values embedded in the control service.

## Current capability

After Rust is installed under `/srv/rust/server`, running `docker compose --env-file .env -f compose.yml up -d rust-server` starts the installed server in a container.

## Out of scope

- RCON management and graceful save/quit
- Readiness checks beyond a running Docker container
- Automatic restart policies
