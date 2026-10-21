# Iteration 113 — protected Catalog Prepare handoff

## Goal

Record the final Stage 08 security boundary: a Catalog root provisioning
command must never execute directly from mutable Updater staging.

## Updated boundary

- Updater `Prepare` retains its existing lifecycle meaning. Its generic
  root-owned Executor re-verifies the prepared inventory and publishes a
  root-owned, non-group/world-writable handoff at
  `/opt/rustserver/<application>/prepared/<tag>/`.
- The handoff is deliberately not a release installation: it changes neither
  `current` nor systemd state and never invokes application provisioning.
- Catalog operators run the independent `host`, `database` and `media`
  commands only from `/opt/rustserver/plugin-catalog/prepared/<tag>/`, then
  request the normal Updater Install. Platform still has no knowledge of
  Catalog providers, connection strings, schema or media root.
- The Catalog-owned `media` operation renders the systemd writable-path
  exception from its configured root; it must run before first activation even
  when it only validates existing media files.

## Deliberately unchanged

- Bootstrap does not perform a VM installation, cleanup, provider setup or
  Catalog provisioning.
- Existing Catalog database, media and configuration state remains untouched;
  the documented compatibility path remains conservative.
