# Iteration 082 — Production service provisioning

## Goal

Prepare runtime dependencies for Control, Versions, Plugins and Snapshots
in a single reusable Production Bootstrap operation.

## Added and updated

- Reviewed systemd units under `deploy/systemd/`.
- Standalone `deploy/bootstrap/provision-services.sh`, also invoked by the
  Production Bootstrap automatically.
- Uses existing Docker Compose assets and Rust configuration template,
  without duplicating them in the repository.
- Ensures Docker/Compose, service accounts, persistent data, game-data ACLs
  and RCON credential delivery for Snapshots.

## Current capability

Production Bootstrap provisions all four additional registered .NET services.
Release installation and activation remain exclusive to Updater.

## Deliberately deferred

Private Git checkout/credentials for Plugins and Configs workspaces.

## Validation

Source-contract review completed. Ubuntu VM integration remains to be run.

## Decisions

Re-running provisioning preserves canonical configuration and state.
No application service is started or enabled by this script.
