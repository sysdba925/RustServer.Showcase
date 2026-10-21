# Iteration 112 — Catalog-owned provisioning boundary

## Goal

Move every Plugin Catalog provider-specific provisioning concern out of
Platform Bootstrap while retaining Catalog's verified Updater registration and
a safe migration path for already partially installed hosts.

## Added and updated

- Removed Platform-owned Catalog PostgreSQL installation, role/database/schema
  creation, SQL artifacts, service user/state directories, runtime provider
  configuration, systemd unit, Development direct-service Bootstrap and direct
  Nginx route.
- Kept the root-owned `RustServer.PluginCatalog` Updater registration and its
  additive migration; Updater still performs only verified release lifecycle
  operations.
- Added the root-owned Nginx server-context include point
  `/etc/rustserver/nginx/applications.d/*.conf`. Application service users
  cannot write it; application-owned snippets must retain explicit
  authorization and loopback-only upstreams.
- Recorded the required initial sequence: `Bootstrap → Prepare → explicit
  Catalog provisioning → Install`. Legacy Catalog topology values are retained
  byte-for-byte but ignored by Platform so repeated Bootstrap remains safe.
- Added the Catalog provisioning-boundary architecture/runbook and an
  `AGENTS.md` with the ownership and non-destructive rerun rules.

## Current capability

Catalog may be registered and prepared by Updater without revealing its
providers to Platform. Its verified artifact is then responsible for host,
database and media provisioning before Updater activates the release. The
base Nginx configuration no longer knows the Catalog route; Catalog owns the
Admin-gated snippet through the protected include point.

## Deliberately deferred

- No VM provisioning, real package installation, data cleanup, release
  publication, provider migration or automatic Catalog provisioning occurred.
- Unknown or locally edited legacy Catalog units/configurations remain manual
  review cases; Bootstrap does not overwrite them.

## Validation

- `bash deploy/bootstrap/tests/bootstrap-tests.sh` passed.
- `bash -n` passed for changed Platform Bootstrap/topology scripts.
- `git diff --check` passed.

## Decisions

- A generic Nginx include point is infrastructure, not an authorization
  boundary: each application route owns its access policy. Catalog requires
  Auth's existing Admin `auth_request` and may proxy only to loopback.
- Updater registration authorizes a release lifecycle only. It never grants
  authority to install PostgreSQL, create storage or invoke an application's
  provisioning commands.
