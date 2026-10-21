# Iteration 067 — Auth deployment integration

## Goal

Integrate the reviewed RustServer.Auth production release contract into the
existing Bootstrap, shared application registry, and privileged Updater
deployment lifecycle without exposing secrets or placing persistent data in
replaceable release directories.

## Added and updated

- Bootstrap now provisions the create-only Auth service identity,
  `/opt/rustserver/auth/releases`, `/var/lib/rustserver/auth`, and the reviewed
  `rustserver-auth.service` unit. It does not create, modify, enable, or start
  Auth itself.
- The application registry defines Auth's repository, release entrypoint,
  service, and `127.0.0.1:5286/health/ready` readiness contract. A normal
  Bootstrap recovery run appends that one registration to a pre-existing INI
  only when absent, preserving all prior content and retaining a timestamped
  backup.
- The Deployment Executor validates externally managed Auth configuration
  before any release copy, symlink switch, or systemd operation. It also checks
  initial-admin requirements for an absent or empty default SQLite database,
  enables the unit only after successful readiness, and leaves Auth data outside
  the release tree.
- Bootstrap and Updater documentation now specify external secret provisioning,
  explicit Prepare/Install activation, persistent database behavior, and the
  database-migration limitation of binary rollback.

## Current capability

An operator can externally provision Auth configuration, prepare a verified
`RustServer.Auth-v<version>-linux-x64.tar.gz` release, and explicitly install,
update, or roll back the service through the existing Updater lifecycle. A
missing or unsafe environment fails safely before activation; failed readiness
restores the previous binary release without replacing or deleting the SQLite
database.

## Deliberately deferred

- Bootstrap does not generate, store, display, or rotate Google or initial
  administrator credentials.
- No automatic database downgrade, restore, deletion, or schema conversion is
  performed during rollback. Operators must verify that a selected prior binary
  is compatible with the database migrations already present.

## Validation

- `bash deploy/bootstrap/tests/bootstrap-tests.sh` — passed.
- `bash deploy/bootstrap/tests/deployment-executor-tests.sh` — passed.
- `bash deploy/tests/install-tests.sh` — passed.
- `dotnet test RustServer.Updater.slnx --no-restore` — passed (89 tests).

## Decisions

- Auth keeps the existing DotNet registry schema, so adding its registration is
  backward-compatible with deployed Updater binaries.
- The Auth environment remains a root-owned external operational boundary;
  Bootstrap can prepare the host safely before secrets are supplied, while the
  executor is the final activation guard.
