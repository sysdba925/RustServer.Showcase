# Iteration 072 — Updater configuration migration

## Goal

Move the persistent Updater registry, release-channel selection, and GitHub
credential from the legacy `/etc/rustserver/updater/` directory to the flat
runtime-configuration layout without losing existing operator values.

## Added and updated

- Production Bootstrap atomically copies legacy-only Updater files to
  `/etc/rustserver/updater-applications.ini`,
  `/etc/rustserver/updater-release-channel`, and
  `/etc/rustserver/updater-github-token`, preserving file contents, owner, and
  mode. Existing canonical files remain authoritative; legacy sources remain
  recoverable backups.
- The Deployment Executor, Updater defaults, and systemd `LoadCredential`
  source now use the canonical paths.
- An ordinary Bootstrap rerun refuses to switch an installed legacy Updater
  binary. The explicit `upgrade-updater` workflow validates that the requested
  release contains the canonical defaults before it stops and restarts the
  service. A true first install has no persistent Updater state and can create
  its canonical configuration without an active release.
- `upgrade-updater` now stages the new Deployment Executor instead of replacing
  the active program in step 65. Once the archive and canonical configuration
  are validated, it records a root-owned Executor rollback copy, stops Updater,
  atomically activates the Executor and Updater, then starts and checks the new
  service. Failed and interrupted transactions restore the previous compatible
  pair; existing two-line transaction records remain readable for recovery.
- Updated the architecture, installer, Bootstrap, Updater, API-contract, and
  release-publishing documentation.

## Deliberately deferred

- Deletion of `/etc/rustserver/updater/` and its retained files.
- Production execution or VM validation of the migration and explicit upgrade.
- Development Bootstrap and all unrelated runtime configuration migrations.

## Validation

- Bash syntax, .NET build, systemd text inspection, and cross-repository path
  reference checks are recorded with their actual results in the change report.
