# Changelog

All notable changes to this project are documented in this file.

## Unreleased

### Added

- Added the unauthenticated `/health/ready` readiness endpoint with SQLite and
  EF Core schema validation.
- Added production listener and persistent SQLite configuration, a systemd unit,
  deployment environment template, and deployment documentation.

### Changed

- Startup now applies EF Core migrations before role and administrator bootstrap.
- Initial administrator credentials are required only for an empty database.
- The production deployment contract now identifies `/etc/rustserver/auth.env`
  as the sole persistent Auth environment file and documents its create-only
  Bootstrap lifecycle, required keys, ownership, and release persistence.
