# Iteration 001 — SteamCMD infrastructure

## Goal

Provide an isolated Docker Compose utility for installing and updating Rust Dedicated Server files on the Linux host.

## Created files

- `docker/steamcmd/compose.yml`
- `docker/steamcmd/.env.example`
- `docker/steamcmd/.gitignore`
- `docker/steamcmd/scripts/update-rust.sh`
- `docs/changelog/iteration-001.md`

## Decisions

- Use the standard `cm2network/steamcmd` image without a custom Dockerfile.
- Keep the host server path configurable through the local `RUST_SERVER_PATH` variable.
- Bind-mount that path to `/server` so server files remain outside the image and container filesystem.
- Use Steam anonymous login and Rust Dedicated Server App ID `258550`; every run performs update and validation, then exits.
- Changelog entries use sequential iteration numbers; future work must be recorded in new files such as `iteration-002.md`.

## Current capability

Running `docker compose --env-file .env -f compose.yml run --rm steamcmd` installs or updates Rust Dedicated Server in the configured host directory.

## Out of scope

- Running the Rust server
- RCON, Oxide, and plugins
- Deployment, monitoring, and Server Control integration
- Platform-wide Docker Compose and Terraform
