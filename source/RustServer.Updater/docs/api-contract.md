# RustServer.Updater API contract

The machine-readable source of this contract is [openapi.json](../openapi.json).

## Implementation status

`GET /health`, the runtime read-only application catalog, durable operation tracking, verified package preparation, and Updater-owned executor-backed deployment are implemented.

| Method | Endpoint | Status | Purpose |
| --- | --- | --- | --- |
| `GET` | `/health` | Implemented | Check whether Updater is running. |
| `GET` | `/emergency` | Implemented | Serve the dependency-free emergency recovery page from the Updater process. |
| `GET` | `/api/emergency` | Implemented | List backend channel state, registered application snapshots, and all permitted releases newest first. |
| `POST` | `/api/emergency/applications/{name}/update` | Implemented | Prepare, verify, stage, and activate a selected channel-permitted release in one operation. |
| `GET` | `/api/applications` | Implemented | List all registered applications and their known state. |
| `GET` | `/api/applications/{name}` | Implemented | Get information about one registered application. |
| `GET` | `/api/applications/registrations` | Implemented | List durable registration definitions without runtime snapshots. |
| `POST` | `/api/applications/refresh` | Implemented | Validate/reload INI, then refresh installed, systemd, and GitHub Releases state without changing applications. |
| `POST` | `/api/applications/{name}/prepare` | Implemented | Asynchronously resolve, authenticate-download, verify, extract, and stage an explicit release without activation. |
| `POST` | `/api/applications/{name}/install` | Implemented | Activate one explicit, already verified package as the first release. |
| `POST` | `/api/applications/{name}/update` | Implemented | Activate one explicit, already verified package for an installed application. |
| `POST` | `/api/applications/{name}/rollback` | Implemented | Reactivate the previous successful release, or an explicit previously installed release. |
| `GET` | `/api/operations` | Implemented | List newest durable operation records; `limit` defaults to 50 and is capped at 100. |
| `GET` | `/api/operations/{id}` | Implemented | Get durable operation state by identifier. |

`POST /api/applications/{name}/prepare` accepts `{ "version": "vX.Y.Z-dev.N" }` (or a stable production tag) and returns `202 Accepted` with an `operationId`. The client polls `GET /api/operations/{id}`. Steps are `Resolving`, `Downloading`, `Verifying`, `Extracting`, `Staged`, and `Securing prepared artifact`. A successful operation includes a result with the application, release tag, staging path, and verified SHA-256. Before success the fixed root Executor independently verifies the inventory and publishes a root-owned, non-group/world-writable copy at `/opt/rustserver/<application>/prepared/<tag>/`; this is a handoff only, does not run a package file or provisioning, and does not activate a release. A second pending or running operation for that application returns `409 Conflict`.

## Emergency interface

The page lists the newest 50 durable operations through `GET /api/operations`.
Each entry opens current details through `GET /api/operations/{id}` and can be
copied as plain text. Details contain the operation identity, application, type,
status, step, timestamps and the error supplied by the API. They do not include
raw result objects, credentials, process output or request headers. A refresh
re-reads durable history, including failed Rollout operations. If history or
details are unavailable the page reports that failure without clearing the
last displayed details. The UI does not infer a cause from a generic error.

`GET /emergency` returns static HTML with only local CSS and vanilla JavaScript; it has no CDN, frontend build, or RustServer.Web dependency. The built-in page exposes **Rollout / Resume Rollout** only while at least one rollout-managed application is not installed. It checks that all missing rollout-managed applications have releases before starting, then sequentially performs existing `POST /api/applications/{name}/prepare` and `POST /api/applications/{name}/install`, polling each durable operation until `Succeeded`. Dependency order: Auth, Rcon, Control, Versions, Plugins, Snapshots, Web; additional registered applications follow. `RustServer.PluginCatalog` remains visible but is excluded from automatic Rollout. Its initial-install controls separate `Prepare` from `Install`; a successful Prepare enables Install only for that page session, leaving the required operator sequence `Prepare → explicit Catalog provisioning → Install`. Existing installations are never updated by Rollout. Failure or interruption stops the browser-side loop; already accepted operations continue independently, and resuming skips installed applications. When all rollout-managed applications are installed, the button is hidden. No new API, executor permission, or persistent rollout operation is introduced; Bootstrap remains responsible for common prerequisites, while Catalog owns its providers, service unit and credentials. Rollout never installs PostgreSQL, creates storage, or invokes provisioning. Its sole discovery endpoint, `GET /api/emergency`, returns the installation-wide `channel`, registered application snapshot data, and `availableVersions` for each registration. Release discovery uses the existing authenticated GitHub client and filters/sorts on the server: Development returns only non-draft prereleases matching `vX.Y.Z-dev.N`; Production returns only non-draft stable releases matching `vX.Y.Z`; both are ordered by the existing Semantic Versioning implementation, newest first. A per-application discovery failure produces an empty version list plus a non-secret `availableVersionsError`, so the page can still use rollback; a local emergency-state failure returns `503`.

`POST /api/emergency/applications/{name}/update` accepts `{ "version": "..." }` only when the tag matches the configured installation-wide channel. It creates one durable Update (or SelfUpdate) operation that runs the existing preparation service, records the verified staging result, validates that staging again through `IPreparedPackageLocator`, and invokes the normal Deployment Executor. It does not accept an unverified deployment. It requires an installed application, so initial installation continues to use the explicit Install endpoint. The emergency page uses the existing rollback endpoint with no request body; that preserves the executor's previous-successful-release rollback semantics and does not equate an arbitrary GitHub release with rollback.

The emergency page introduces no bind-address or authentication change. The current API has no application-level authentication; deployment endpoints, the page, and its endpoints all rely on the existing host/network access boundary. Deployments must keep that listener private or apply authenticated reverse-proxy/network access before exposing it outside trusted administration.

## Durable operations

An operation contains `id`, `applicationName`, `type`, `status`, `step`, target and previous version, creation/start/completion timestamps, an optional error, and an optional result. Types are `Prepare`, `Install`, `Update`, `Rollback`, and `SelfUpdate`. The collection endpoint includes terminal records, including `Failed` and `Interrupted`, so clients can retain a complete execution history.

The allowed state transitions are:

```text
Pending -> Running | Failed | Interrupted
Running -> Succeeded | Failed | Interrupted
```

`Succeeded`, `Failed`, and `Interrupted` are terminal. A current step may only be changed while an operation is `Pending` or `Running`. Only one pending or running operation may exist for an application at a time.

Operation records are stored one per JSON file in `Operations:StoragePath`, which defaults to `/var/lib/rustserver/updater/operations`. A state change is serialized to a flushed temporary file in that directory and atomically moved or replaced into its final `<operation-id>.json` file. A malformed JSON file is logged and ignored without preventing valid records from loading. File write failures are returned to the caller; a lifecycle change is never reported as successful before its replacement record is durable.

At startup, ordinary in-process work left `Pending` or `Running` becomes `Interrupted`. Executor-owned Install, Update, Rollback, and SelfUpdate work is different: the API first reads the root-owned executor transaction. A terminal executor outcome is copied into the operation (including the `deployment` result); an active executor transaction remains active and is polled. Only a deployment with no executor transaction is interrupted. The API never treats successful process launch as deployment success.

The lifecycle manager persists `Running` before it invokes supplied work. Long-running work receives an operation-owned cancellation token; an HTTP request-abort token must not be used for that token after the request has been accepted. The Prepare worker stores its result while still running, then marks the operation successful, so a package is never reported as prepared before both its staging manifest and operation result are durable.

The catalog is stored only in `/etc/rustserver/updater/applications.ini`, outside versioned Updater binaries. It is a durable runtime file with no API write path; initial provisioning or migration may create it, but runtime does not read Bootstrap or Installer files. `[Defaults]` defines `RepositoryOwner`, `InstallRoot`, and health scheme/host/path. Each `[Application:<name>]` supplies a repository name, one directory segment, health port, and an optional `HealthPath` override. `Type` defaults to `DotNet` when omitted: DotNet requires a service name without `.service` and a DLL entrypoint, while StaticWeb requires `EntryFile=index.html` and forbids service and entrypoint. Parsing resolves repository, installation path, optional systemd unit, and IPv4/IPv6 readiness URL with a URI builder. It rejects substitutions, paths or percent-encoding that can traverse, invalid or mixed type-specific values, duplicate names/installation paths, duplicate populated systemd units, and paths outside `InstallRoot`.

`GET /api/applications/registrations` now returns `type`, nullable `serviceName` and `entrypoint`, and nullable `entryFile` in addition to the existing registration fields. Existing INI entries without `Type` continue to produce `type: "DotNet"`. StaticWeb packages may now be prepared and verified, but must not be installed, updated, or rolled back until the executor and static-serving stages introduce matching policies; do not add a StaticWeb registration to the live registry yet.

There are no registration write endpoints. Add or change a registration through reviewed SSH access to the INI; changing the INI does not install an application. The initial snapshot is built at startup from the `current` symbolic link and `systemctl is-active --quiet`; it does not contact GitHub. When the link is absent or invalid, `installedVersion` is `null` and `status` is `NotInstalled`. systemd failures produce `Unknown` rather than an HTTP error.

`POST /api/applications/refresh` first validates and atomically publishes a new INI snapshot, then repeats the local checks and retrieves GitHub Releases. It makes no installation, service, or configuration changes. An invalid or unreadable INI leaves the prior valid GET snapshot available, returns a diagnostic `503`, and causes Prepare/Install/Update/Rollback admission to fail rather than use stale authorization. GitHub errors keep the previously known available version rather than clearing it. Concurrent refreshes are serialized.

Development discovers GitHub prereleases tagged `vX.Y.Z-dev.N`; Production discovers stable tags `vX.Y.Z`. This is an installation-wide characteristic, stored once in `/etc/rustserver/updater-release-channel`, not a per-application API setting. The original release tag is returned, and version precedence follows Semantic Versioning, including prerelease ordering. `updateAvailable` is derived from known installed and available versions, and is always `false` when the application is not installed.

For private GitHub Releases, initial provisioning may place the separate Updater token at `/etc/rustserver/updater-github-token` with mode `0600`. The systemd unit supplies it with `LoadCredential`; the service reads only the private credential copy exposed by systemd. The token is not placed in application configuration, an API response, a URL, a command line, or logs. Prepare uses the identical provider: it gets exact-tag metadata and asset API URLs, sends `Authorization: Bearer` only to the configured GitHub API origin, and follows HTTPS asset redirects without forwarding that header. `browser_download_url` is not used. Local GET endpoints continue working if the credential is absent. Refresh returns `503 Service Unavailable` with a non-secret diagnostic and preserves the previous available versions; an accepted Prepare operation becomes `Failed` with a non-secret error.

## Package staging

Prepare derives exactly two assets from every registration name and requested tag: `<ApplicationName>-<tag>-linux-x64.tar.gz` and the same filename plus `.sha256`. The selected tag must match the installation-wide channel (`vX.Y.Z-dev.N` for Development, `vX.Y.Z` for Production), and GitHub release metadata must agree. Download and expansion limits come from `PackagePreparation` configuration. Archives must be valid tar.gz streams, contain no absolute or traversal paths, duplicate/conflicting paths, symlinks, hardlinks, or special files. DotNet archives must contain the registered DLL entrypoint; StaticWeb archives must contain the registered `EntryFile` (`index.html`). The required file may be at the archive root or beneath exactly one top-level directory. The selected root, with its complete distribution tree, is staged and covered by the same immutable manifest inventory. Preparation neither builds StaticWeb source nor runs npm commands.

The default persistent staging root is `/var/lib/rustserver/updater/staging`; the final path is `/var/lib/rustserver/updater/staging/<application>/<tag>`. The directory contains an atomically written preparation manifest with an exact link-free inventory of directories and files (path, type, size, and SHA-256). A changed, added, removed, or linked staging entry is ineligible for activation. For the public Prepare endpoint, the root Executor validates this inventory again and atomically copies it to the generic protected handoff `/opt/rustserver/<application>/prepared/<tag>/`; that directory is root-owned with no group/other write permission, but is neither a release nor an activation. Temporary downloads and extraction occur below the configured staging root and are cleaned on failure. Preparation never writes to `/opt/rustserver/<application>/releases/`, changes an application's `current` symlink, starts/stops systemd, runs provisioning, or builds source code on Ubuntu.

Manifests created before this inventory was introduced are not accepted for deployment; prepare the requested release again to replace the old staging package.

## Deployment Executor

Install and Update accept `{ "version": "vX.Y.Z..." }`; Rollback optionally accepts the same body to select a specific already-installed release. All three return `202 Accepted` with `operationId`. They do not download packages: a missing, damaged, or mismatched Stage 6 manifest returns a clear `409 Conflict` before a deployment is accepted.

The API writes a request identified solely by the operation UUID, then sends that UUID to the group-restricted `/run/rustserver-deployment-executor.sock`. systemd socket activation starts the root-owned executor from the active Updater bundle; the Updater has no sudo rule and keeps `NoNewPrivileges=true`. The executor accepts no API paths, units, commands, or readiness URLs. Immediately before each privileged operation it parses and validates `/etc/rustserver/updater/applications.ini` and resolves the requested application from it. Consequently, HTTP requests and mutable staging never grant root deployment authority.

The executor stores an atomic transaction record in `/var/lib/rustserver/updater/deployments/<operation-id>.json`, takes `/run/lock/rustserver/updater.lock`, checks the complete Stage 6 inventory before copying it to `releases/<tag>`, atomically switches `current`, and performs the registered HTTP readiness probe. Any failed activation restores the previous `current`, restarts it, and verifies its readiness. Releases are never deleted. Ordinary deployment starts services normally; only self-update and recovery use `systemctl start --ignore-dependencies`.

The socket-activated worker is a root-owned systemd service from the active release, so it remains alive when a self-update restarts `rustserver-updater.service`. A boot-time `rustserver-updater-recovery.service` scans incomplete root transactions before Updater starts and rolls them back. A self-update requires the paired bundle manifest and moves API and Executor together; Bootstrap is limited to first-bundle provisioning and migration.

First-install hook failures are persisted in `error` with the exit code and validated, owner-supplied stage, category and reason identifiers. Older hooks or invalid/missing diagnostics use `unknown` identifiers. The deployment output provides bounded, best-effort redacted stdout and stderr separately from this safe error.

Provisioning output is available automatically in `deployment.provisioningOutput`. The Updater does not provide authentication: protect the listener and all proxy routes for trusted administrators. Redaction is best effort and diagnostics must be treated as sensitive. The deployment result includes the target version and hook exit code.
