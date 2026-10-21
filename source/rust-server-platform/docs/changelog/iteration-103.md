# Iteration 103 — Install NetworkAccess helper with Control release

## Goal

Make first-time Control rollout independent of a dangling Bootstrap-owned
systemd link, while preserving root isolation and fail-closed network behavior.

## Added and updated

- Bootstrap still provisions nftables, kernel module loading, runtime directories,
  Control user/group, and reviewed Control service unit, but no longer installs
  or enables the release-owned NetworkAccess helper.
- The root-owned Updater Executor validates the helper unit and firewall guard
  from the Control release, creates its stable systemd unit link, reloads
  systemd, and starts Control with ordinary dependency resolution.
- The Executor enables the helper for boot only after Control readiness. It
  stops the helper on updates, reloads unit definitions on rollback, and
  removes a newly registered unit if the first Control activation fails.
- Control remains an unprivileged service and uses its root-only local helper
  via Unix socket. There is no independent Helper release or extra HTTP API.

## Deliberately deferred

- No changes to other application deployment policies or public API contracts.
- No new standalone NetworkAccess microservice.

## Validation

- Updated Bootstrap source-level contract test and added Updater rollout tests.
- Repository changes were inspected; .NET and Ubuntu systemd integration tests
  must still be run in the development environment before publishing releases.

## Decisions

- Updater Executor owns Control release unit installation and activation.
- Bootstrap owns operating-system prerequisites only.
- systemd owns process privileges and startup dependencies; Control owns the
  NetworkAccess commands sent to its helper.
