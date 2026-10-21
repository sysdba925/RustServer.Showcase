# Iteration 068 — Runtime configuration architecture approval

## Goal

Record the approved canonical runtime configuration layout and prepare the
repository documentation and migration inventory without changing runtime
behavior.

## Added and updated

- Added the source-backed runtime configuration migration inventory, including
  current file locations, readers and writers, systemd and Compose dependencies,
  affected repositories, validation, and cleanup criteria.
- Linked Bootstrap, operational, runbook, and architecture documentation to
  the canonical runtime configuration contract and labeled checkout-local and
  `/etc/rustserver/updater/` paths as current pre-migration behavior.

## Current capability

The current development and production paths remain unchanged. Operators can
continue to use their existing Bootstrap and development instructions without
manually moving configuration, secrets, runtime assets, or service units.

## Deliberately deferred

- Migrating any configuration or secret to the flat `/etc/rustserver/` layout.
- Changing Bootstrap, systemd units, Docker Compose, installed runtime assets,
  release artifacts, or application runtime behavior.
- Removing checkout-local or updater-subdirectory configuration paths.

## Validation

- Read the approved runtime configuration layout in full.
- Searched platform and sibling application sources for configuration-path,
  systemd, Docker Compose, RCON, Updater, Control, Auth, and Snapshots
  dependencies.
- Verified documentation links and recorded unresolved contract conflicts for
  explicit architectural resolution before implementation.

## Decisions

- `docs/architecture/runtime-configuration-layout.md` is the canonical target
  for Development and Production; approval does not mean that migration is
  complete.
- Implementation must use the migration inventory and resolve its documented
  Auth, Bootstrap, Control-state, Snapshots/RCON, and permission-model questions
  before changing runtime behavior.
