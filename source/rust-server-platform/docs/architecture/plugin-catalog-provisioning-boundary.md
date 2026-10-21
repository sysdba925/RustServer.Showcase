# Plugin Catalog provisioning boundary

Status: **implemented Platform boundary; Catalog artifact owns its dependency
provisioning** (17 October 2026).

## Ownership

`rust-server-platform` owns the common host substrate: `/opt/rustserver`, the
root-owned Updater application registry, Nginx itself, the base Admin
`auth_request` endpoint, and a protected application-snippet include point.
It does not own a Plugin Catalog database provider, connection string, role,
database, schema, media provider, media root, Catalog service identity, or
Catalog systemd/Nginx configuration.

`RustServer.PluginCatalog` owns those Catalog-specific values and ships the
explicit `host`, `database`, and `media` provisioning operations in its verified
release artifact. Each operation remains independently invocable for recovery. The Catalog-owned
`install` command runs host → database → media in order on first deployment;
none starts the Catalog API or performs release activation. An unsupported provider is a
clear failure, never a fallback.

Updater retains the `RustServer.PluginCatalog` registration in
`/etc/rustserver/updater/applications.ini` with an explicit, audited
`InstallHook=provisioning/provision.sh` opt-in. This is not a PostgreSQL or
Local media command: it instructs the generic privileged Updater Executor to
run the verified Catalog-owned `install` script from the protected prepared
artifact **only on the first Install**.

## Required initial-install order

```text
Platform Bootstrap
  -> Updater Prepare (download, verify, secure Catalog artifact)
  -> Updater Install:
       Catalog install hook (host -> database -> media)
       activate release and verify /health/ready
```

The operator presses **Rollout** once. The Updater emergency page now includes
Catalog in its initial install order; browser requests still use the existing
Prepare and Install APIs, without a new privileged HTTP endpoint. No manual
provider-setup button, SQL, schema, data migration or provider selection
is added to Platform or Updater.

A failed first install may rerun the Catalog hook, which preserves existing
role, database, schema and media. The Executor may reuse an existing immutable
release directory **only when it exactly matches the verified package**;
otherwise it fails closed. Missing old-release provisioning is a clear
pre-activation failure, not an attempt to stop an uncreated systemd unit.
Later Update/Rollback must not invoke the first-install hook.

## Nginx application extension point

The Platform Nginx server includes only root-owned regular snippets from
`/etc/rustserver/nginx/applications.d/*.conf`. Bootstrap creates that directory
as `root:root` mode `0750`; service identities cannot modify it. The base
configuration supplies the internal Auth Admin `auth_request` endpoint but no
Catalog route or Catalog upstream.

Catalog host provisioning validates that the Platform include point exists,
installs its own root-owned snippet there, validates the complete Nginx
configuration, and reloads Nginx only after a valid result. The Catalog
snippet keeps `/plugin-catalog-api/` behind the Admin subrequest and proxies
only to loopback Kestrel. No Catalog port is exposed by Platform Nginx or by
the Catalog service unit.

## Conservative transition from legacy Platform ownership

An existing host can already contain the legacy Platform-managed Catalog
PostgreSQL role/database, media directory, inline systemd environment,
base-Nginx route and deprecated topology entries. Do not remove, reset,
migrate, or recreate those resources. Bootstrap accepts but does not export
the legacy topology keys, leaving the file byte-for-byte intact.

1. Run updated Platform Bootstrap to expose the generic root-owned Nginx extension and InstallHook registry entry.
2. Refresh the Updater runtime/registry, then use Rollout to Prepare and Install a new Catalog release.
3. The Catalog-owned install hook handles recognized legacy systemd/Nginx configuration conservatively; an unknown or edited unit or conflicting provider configuration fails closed.
4. Database and media are validated/preserved. No automatic provider migration or deletion occurs. Review the Catalog executor journal if the installation fails.

