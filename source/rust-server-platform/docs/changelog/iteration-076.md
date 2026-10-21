# Iteration 076 — Bootstrap configuration-directory compatibility

## Goal

Allow Development and Production Bootstrap to use the approved shared flat
`/etc/rustserver/` configuration layout on the same host without requiring a
common service account.

## Added and updated

- Production Bootstrap now maintains `/etc/rustserver` as `root:root`, mode
  `0755`, matching the traversal requirement of Development service users.
- Individual Updater, Auth, and secret files retain their existing
  least-privilege ownership and modes; no configuration values or persistent
  state are replaced.
- Updated the runtime-configuration and operator documentation to describe the
  shared-directory and per-file permission boundary.

## Deliberately deferred

- Runtime validation on a disposable VM.
- Any change to application service accounts, deployment transactions, or
  rollback behavior.

## Validation

- Static Bootstrap integration audit and syntax/path/permission checks are
  recorded in the Stage 9 report.
