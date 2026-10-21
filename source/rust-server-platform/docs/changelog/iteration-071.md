# Iteration 071 — Remove persistent Production Bootstrap configuration

## Goal

Remove `/etc/rustserver/bootstrap.env` from the Production Bootstrap runtime
configuration boundary while retaining version-controlled installation defaults.

## Added and updated

- Production Bootstrap now parses
  `deploy/bootstrap/bootstrap.env.example` from its downloaded archive on each
  invocation. The parser remains data-only, rejects duplicate or unsupported
  keys, and applies supported same-named environment overrides for that one
  invocation.
- Removed creation, preservation, and reading of
  `/etc/rustserver/bootstrap.env`. Existing files are intentionally ignored and
  left untouched for a separate cleanup migration.
- Preserved the explicit `upgrade-updater <release-tag>` contract: the supplied
  tag still derives the archive and checksum names and overrides installation
  defaults for that operation.
- Kept `/etc/rustserver/updater.env` create-only. A pre-existing listener
  configuration remains authoritative and is never overwritten on a rerun.
- Updated the Production Bootstrap guide and runtime-configuration architecture
  inventory to distinguish installation defaults from persistent runtime
  configuration.

## Validation

- Ran Bash syntax checks for the Production Installer, Bootstrap entry point,
  common library, and numbered Production Bootstrap steps.
- Ran `git diff --check`.
- Did not run the Installer, Bootstrap steps, automated tests, or make VM
  changes.
