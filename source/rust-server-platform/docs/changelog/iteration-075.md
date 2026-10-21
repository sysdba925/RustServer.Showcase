# Iteration 075 — Auth runtime configuration

## Goal

Standardize the persistent production configuration contract for
`RustServer.Auth` without changing its authentication flow, credentials, or
SQLite data.

## Added and updated

- Added `/etc/rustserver/auth.env` to the flat runtime-configuration contract
  as the sole persistent Auth environment file.
- Documented the required Google credentials, first-database administrator
  inputs, `root:rustserver-auth` ownership, and `0640` mode.
- Documented the create-only Bootstrap behavior: a missing file leaves Auth
  unstarted, while an existing file is authoritative and remains untouched.
- Recorded that Auth releases preserve both `auth.env` and
  `/var/lib/rustserver/auth/auth.db`.

## Deliberately unchanged

- Auth APIs, authentication flows, Google OAuth behavior, and SQLite schema.
- The shared `/etc/rustserver/` directory ownership and permissions.
- Updater, Deployment Executor, and unrelated services.

## Validation

- Bash syntax, whitespace, and canonical-path checks are recorded in the
  Stage 8 change report.
