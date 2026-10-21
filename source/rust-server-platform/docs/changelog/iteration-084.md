# Iteration 084 — Start verified services after a host reboot

## Goal

Ensure installed .NET platform services continue to start after a normal Ubuntu
reboot. Provisioning must not enable a service before a real release has been
installed and passed readiness verification.

## Added and updated

- `RustServer.Updater.Executor` now invokes `systemctl enable` for a registered
  .NET service after activating a release and verifying its readiness.
- Rollback restoration also enables the restored, readiness-verified .NET service.
- `RustServer.Updater` itself remains Bootstrap-owned; the executor does not
  alter its self-update boot enablement.
- The systemd boundary validates fixed service unit names and does not accept
  service names from HTTP requests.
- Deployment integration tests cover both first Install and Update, asserting
  that service enablement follows normal start and succeeds only on the
  readiness-verified path.

## Current capability

The source implementation now ensures subsequent successful Updater-managed
.NET installs, updates, or successful rollback restorations enable automatic
service startup after reboots. Existing services that were installed before
this change still need a one-time repair or redeployment after the new Updater
release is installed.

## Deliberately deferred

- This repository change does not deploy Updater on an existing VM or repair
  already disabled services automatically.
- A specific `502 Bad Gateway` must still be confirmed with systemd state
  or journal output if restoring service enablement alone does not resolve it.

## Validation

- Compared Production Bootstrap Auth provisioning against Updater's ordinary
  `systemctl start` path; verified the missing enablement in existing source.
- Updated executor integration and invariant test coverage.
- Source updates committed to `RustServer.Updater/develop`.
- The .NET test suite and live VM boot test have not been run from this session.

## Decisions

- Bootstrap provisions units and state; it does not activate uninstalled
  application releases.
- Updater Executor owns enabling a registered `.NET` service only after a
  verified release becomes ready. Nginx and the Updater's own boot service
  remain separate responsibilities.
