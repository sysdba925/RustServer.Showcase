# Iteration 073 — Control configuration migration

## Goal

Move Control's persistent desired/effective server-configuration JSON from the
development checkout to `/var/lib/rustserver/control/server-configuration.json`.

## Added and updated

- Made the canonical `/var/lib/rustserver/control/server-configuration.json` path
  the Control default and the explicit Development service/direct-runner path.
- Added a Development Bootstrap migration that preserves an existing canonical
  file as authoritative. When it is absent, Bootstrap atomically copies the
  legacy JSON bytes and retains the checkout-local source as a recoverable
  backup.
- Created a dedicated Control state directory owned by the Control service user
  so it can create a same-directory temporary file and atomically replace its
  own state on save without changing shared `/etc/rustserver` access.
- Updated the Control design, Development Bootstrap guide and runbook, runtime
  architecture, and operator instructions.

## Deliberately unchanged

- Desired/effective state semantics, the Control HTTP API, and Rust lifecycle
  behavior.
- Deploy Bootstrap, RCON password handling, topology, and unrelated services.

## Validation

- See the Stage 6 change report for the requested Control build, Bash syntax,
  path-consistency, and diff checks.
