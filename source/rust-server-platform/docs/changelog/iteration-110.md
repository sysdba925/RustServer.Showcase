# Iteration 110 — Plugin Catalog platform deployment boundary

## Goal

Make the independently owned Plugin Catalog deployable through the existing
Platform Bootstrap, Updater, Nginx, and development topology without turning a
production host into a source checkout or assigning Catalog any Plugins or Rust
lifecycle authority.

## Added and updated

- Registered `RustServer.PluginCatalog` as a .NET Updater application at
  `/opt/rustserver/plugin-catalog`, with loopback health at
  `127.0.0.1:5116/health/ready`.
- Added an unprivileged Catalog systemd identity, dedicated peer-auth
  PostgreSQL database, versioned initial schema verification, and persistent
  media root outside release trees.
- Added the admin-protected `/plugin-catalog-api/*` Nginx route and an 11 MiB
  proxy body limit for Catalog's 10 MiB upload contract.
- Added Catalog development topology, source synchronization, Release build,
  local development PostgreSQL/media provisioning, and readiness-checked
  systemd flow.
- Added Linux framework-dependent release packaging guidance and a publisher
  that validates release contents before GitHub publication.

## Current capability

Once a reviewed Catalog GitHub Release is available, an operator can use the
existing Updater `Prepare → Install` flow to activate it. Browser Catalog
requests use the same-origin admin-protected route; the service itself remains
loopback-only. Bootstrap safely adds the missing registry entry on a rerun and
otherwise preserves the existing registry.

## Deliberately deferred

- Stage 09 clean-VM deployment, real PostgreSQL/media/API smoke, Admin gateway
  browser smoke, and Updater rollback remain separate work.
- No GitHub Release was published and no VPS was changed in this iteration.
- The current Installer still selects mutable `develop`; production artifact
  independence cannot be claimed until its repository-branch invariant is
  corrected by its owning workflow.

## Validation

- Catalog unit/integration tests, Web lint/typecheck/tests/build, and Platform
  Bootstrap tests were run locally.
- PostgreSQL schema smoke and Nginx configuration validation were not run:
  the local Docker daemon and Nginx executable were unavailable.
- Updater tests compile, but two existing Control helper integration tests fail
  on this Windows host while creating and cleaning a privileged systemd
  symlink; the Catalog rollout-order assertion passes.

## Decisions

- Catalog metadata/media remain independent from RustServer.Plugins Git,
  deployment, and runtime workflows.
- Existing non-empty or incompatible Catalog databases are rejected read-only;
  Bootstrap never uses startup schema creation, migrations, drop, or recreate.
- `GET /health` remains strict liveness, while `/health/ready` is the Updater
  dependency check and is not a CRUD contract.
