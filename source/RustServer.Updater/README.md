# RustServer.Updater

The built-in `/emergency` Updater Console is served from the Updater API and ships
with its local static assets. It remains available without RustServer.Web. Its
Refresh button reloads the registry and local status through
`POST /api/applications/refresh`, then discovers channel-filtered releases via
`GET /api/emergency`. Operation polling reads individual durable records and does
not repeatedly scan releases. The console has no application-level authentication;
keep the existing network access restrictions and never expose it publicly.

Initial rollout is browser-coordinated in the fixed platform order. Closing the
page stops scheduling future services, but accepted operations continue on the
server. Retry checks durable operations and installed versions before continuing.
In-page alerts are transient; acknowledging them does not alter operation records.

`RustServer.Updater` reports the installed, runtime, and available-release state of registered RustServer applications and retains durable records for asynchronous deployment operations.

## Current scope

The current HTTP surface exposes:

```http
GET /health
GET /emergency
GET /api/emergency
POST /api/emergency/applications/{name}/update
GET /api/applications
GET /api/applications/{name}
GET /api/applications/registrations
POST /api/applications/refresh
POST /api/applications/{name}/prepare
POST /api/applications/{name}/install
POST /api/applications/{name}/update
POST /api/applications/{name}/rollback
GET /api/operations
GET /api/operations/{id}
```

`GET /health` returns `200 OK` with `{ "status": "Healthy" }` when the process is running.

Application registrations live only in the Updater runtime registry, `/etc/rustserver/updater/applications.ini`; neither the API nor the Updater process can change the allowed-application list. Every registration resolves `RepositoryOwner + Repository`, `InstallRoot + Directory`, and a readiness URL centrally. `DotNet` registrations additionally resolve `Service + .service` and a DLL entrypoint; `StaticWeb` registrations use `EntryFile=index.html` and have no application service. Omitted `Type` remains backward-compatible and resolves to `DotNet`. The parser rejects traversal, encoded paths, invalid owner/repository names, duplicate names/paths (and duplicate systemd units when present), malformed hosts and ports, and any `${...}` value. `GET /api/applications/registrations` returns the resolved read-only registrations.

`POST /api/applications/refresh` validates and rereads the INI before refreshing local and GitHub state. An invalid file does not replace the last good GET view, but Refresh returns `503` and Prepare/Install/Update/Rollback are refused until the INI is valid again. The root Deployment Executor independently validates the same file immediately before its privileged operation.

To add a prepared application manually, connect through SSH and edit the root-owned file, then refresh. Registration authorizes verified discovery and deployment. An optional audited `InstallHook=provisioning/provision.sh` is a separate, root-owned registry opt-in which permits only the **first Install** to invoke a verified application-owned hook; it does not run during Bootstrap, discovery, Update, Rollback or API startup.

```ini
[Application:<ApplicationName>]
Type=DotNet
Repository=<RepositoryName>
Directory=<single-directory-segment>
Service=<systemd-service-without-.service>
Entrypoint=<safe-entrypoint.dll>
HealthPort=<1-65535>
```

**Deployment order:** on an existing VM install the new Updater bundle before
Bootstrap migrates its registry to include `InstallHook`, otherwise the old
INI parser rejects the new key. On a clean VM the new Bootstrap downloads the
new Updater first. Publish a new Catalog development release before Rollout.

### Catalog first-install hook

`RustServer.PluginCatalog` registration opts into
`InstallHook=provisioning/provision.sh`. The Platform only declares the
audited hook path; Updater controls Prepare and activation but knows no
PostgreSQL, schema, media path or provider. Catalog's `install` command runs
host → database → media in sequence from the root-owned protected package,
then Updater starts the service and checks `/health/ready`. Existing
database/media/configuration are preserved on retry, and conflicts fail closed.
The original Catalog `v0.1.0-dev.1` release predates the hook and cannot be
automatically installed. Publish a newer verified Development release.

### RustServer.Auth

Initial provisioning supplies the reviewed Auth registration: repository `RustServer.Auth`,
directory `auth`, service `rustserver-auth`, entrypoint `RustServer.Auth.dll`,
and readiness `http://127.0.0.1:5286/health/ready`. Its release assets follow
the normal `<ApplicationName>-<tag>-linux-x64.tar.gz` plus `.sha256` convention.
Initial provisioning creates the account, systemd unit, release root, and persistent
state directory but never creates or changes `/etc/rustserver/auth.env`; the
running Updater does not consult provisioner files.

Before an Auth activation, the root executor requires the externally managed
environment file with non-empty Google client variables; an absent or unsafe
file fails before release copying, link switching, or systemd operations. For
an absent or empty default SQLite database, it also requires the initial admin
variables. A successful first Auth installation enables the verified unit for
future boots. Auth data stays at `/var/lib/rustserver/auth/auth.db` and is
never part of a release tree or rollback. Binary rollback is therefore only
safe when the selected prior Auth release remains compatible with the current
database migrations; the executor requires its normal readiness proof and
reports an incompatible rollback as failed.

`Type` is optional for existing .NET registrations. A `StaticWeb` registration instead requires `EntryFile=index.html` and forbids `Service` and `Entrypoint`; it may override the default `HealthPath`. StaticWeb activation is implemented by the Deployment Executor, but do not add one to the live INI or deploy it until Stage 04 configures its Nginx serving and release-identifying readiness route. The common `[Defaults]` section must already contain its owner, install root, and health scheme/host/path. After a reviewed edit, run `sudo systemctl restart rustserver-updater` only if necessary and call `POST /api/applications/refresh`; use Prepare followed by Install explicitly to deploy a release.

The release channel is one persistent setting for the installed Updater instance: `/etc/rustserver/updater-release-channel` contains `Development` or `Production`. It applies to every registration; it is not an API field. The server performs a local installed-version and type-specific runtime check at startup. `GET` endpoints return that in-memory snapshot; only `POST /api/applications/refresh` contacts GitHub Releases and atomically replaces it. Refresh never installs, stops, or restarts an application.

## Emergency interface

`GET /emergency` is a deliberately small, dependency-free recovery page served by the Updater process itself. It also offers a **Rollout** button beside Refresh while rollout-managed applications have no installed release. Rollout runs sequential **Prepare → Install** through the existing endpoints for Auth, Rcon, Control, Versions, Plugins, Snapshots, **PluginCatalog**, and Web, skipping already installed entries. The Catalog card uses the same normal Install action as all other applications. When it is installed for the first time, the root Executor runs its audited, Catalog-owned first-install hook before activation; the operator does not need separate Host/Database/Media buttons. It preflights release availability, polls each durable operation until success, stops at the first failure, and changes to **Resume Rollout** when a partial installation is present. After every rollout-managed application is installed, the button disappears. This is page-local orchestration: navigation or browser closure stops submitting further installs, but already accepted operations continue independently. Use Resume Rollout after returning. It does not alter existing installations or add a new privileged HTTP endpoint. On the explicitly opted-in Catalog first Install, the generic Executor delegates provisioning to the Catalog-owned script; ordinary Rollout entries, Update and Rollback never run that hook. It queries `GET /api/emergency`, which reports the backend's installation-wide channel, the current registered-application snapshots, and all non-draft releases allowed by that channel in semantic-version order (newest first). Development lists only prereleases tagged `vX.Y.Z-dev.N`; Production lists only stable `vX.Y.Z` releases. The page does not implement release filtering or version comparison. If GitHub release discovery is unavailable, the backend returns a non-secret per-application error with no selectable versions, but keeps the existing rollback control usable.

Its Update button calls `POST /api/emergency/applications/{name}/update` with the selected tag. One durable Update or SelfUpdate operation performs the established verified preparation pipeline, re-checks the Stage 6 manifest, and then invokes the same privileged deployment executor as the normal deployment endpoints. It requires an existing installed release. The page's Rollback button calls the existing rollback endpoint without a target version, so the executor selects the previous successful installed release; selecting an older GitHub release is never treated as a rollback. The page only polls durable operation records and renders their status/error text.

No API bind address or authentication policy is changed by this page. The current application has no application-level authentication; `/emergency` and its API endpoints use the same hosting/network boundary as the pre-existing privileged deployment endpoints. Production hosting must therefore keep the Updater listener private (for example, loopback or a restricted management network) or enforce authenticated access in the existing reverse-proxy/network policy before exposing it beyond trusted administrators.

## Release package preparation

`POST /api/applications/{name}/prepare` accepts an explicit version, for example `{ "version": "v0.1.0-dev.2" }`, and returns `202 Accepted` with an `operationId`. The work continues after the HTTP request finishes; poll `GET /api/operations/{id}` for its status and, on success, `result` (`applicationName`, `releaseTag`, `stagingPath`, and `verifiedChecksum`). After staging, the root Executor re-verifies the immutable manifest and copies a link-free, root-owned, non-group/world-writable handoff to `/opt/rustserver/<application>/prepared/<tag>/`; this is still Prepare, not Install, and it does not start a service or run application provisioning. Only one pending or running operation is allowed for an application.

Preparation uses the same release pipeline for `DotNet` and `StaticWeb`: it downloads `<ApplicationName>-<tag>-linux-x64.tar.gz` and its `.sha256` asset through the authenticated GitHub Releases API. A DotNet archive must contain the registered DLL entrypoint; a StaticWeb archive must contain its registered `EntryFile` (`index.html`). In either case, the required file may be at the archive root or inside one single top-level directory; that package root, including all of its files and subdirectories, is staged. Development only permits `vX.Y.Z-dev.N`; Production only permits `vX.Y.Z`. The service streams downloads with bounded sizes, verifies SHA-256, rejects malformed tar.gz files, traversal/absolute/duplicate paths, links and special files, and applies expansion limits. It never builds a StaticWeb application or runs npm commands. A failed preparation removes its temporary work area and leaves installed versions untouched.

Verified content is atomically staged below `PackagePreparation:StoragePath`, by default `/var/lib/rustserver/updater/staging/<application>/<tag>/`. The durable manifest records the complete link-free tree (paths, types, sizes, and SHA-256 hashes), so damaged staging is rejected before activation. `POST .../prepare` also asks the fixed root Executor to verify that inventory again and atomically publish the same package below `/opt/rustserver/<application>/prepared/<tag>/`, with group/other write bits removed. This protected handoff exists for applications that require an explicit root-operated step before activation; the Executor does not inspect or execute those artifacts. Prepare does not write `/opt/rustserver/<application>/releases`, alter `current`, install a release, restart systemd, or invoke provisioning.

Staging created before Stage 7.1 has no contents inventory and is intentionally ineligible for activation; prepare that exact version again before installing it.

## Deployment activation and recovery

Install and Update take an exact package version which must already be present as a verified Stage 6 staging package; they never trigger an implicit download. Rollback selects the previous successful release by default, or a named previously installed release. Each endpoint returns `202 Accepted` and an `operationId`.

The non-root ASP.NET Core process cannot perform arbitrary privileged work. It passes only a durable operation UUID to the root-owned, fixed-function Deployment Executor packaged with `RustServer.Updater`. The executor resolves the UUID against the Updater runtime INI immediately before activation and uses the resolved installation root, type-specific service unit or entry file, and readiness URL. Those values never cross the HTTP or staging privilege boundary as authority.

The service writes only the UUID request and sends that UUID over `/run/rustserver-deployment-executor.sock`; systemd socket activation starts a root-owned worker from the same active Updater bundle. No sudo capability is granted and `NoNewPrivileges=true` remains in force for the API. The executor verifies the Stage 6 inventory. For an explicitly registered InstallHook on the first Install, it re-verifies the root-owned protected Prepare artifact, compares its manifest to staging, and runs the fixed `install` command from that artifact before switching `current`. A missing script or rejected provider fails before activation; subsequent Update/Rollback do not run it. After failure the same immutable release directory can be reused only if its verified content matches exactly, without overwriting it. The executor records every transaction under `/var/lib/rustserver/updater/deployments/`, and atomically switches `current`. A self-update requires a manifest-bound API + Executor bundle and activates both through that one link; any activation or readiness failure restores the entire preceding bundle. Ordinary deployments use normal `systemctl start`; `--ignore-dependencies` is reserved for self-update and recovery. The socket worker survives an API restart, and the bundled boot-time recovery unit completes rollback before the Updater service starts. See [the ownership architecture](docs/architecture.md).

## Durable operation records

`GET /api/operations?limit=50` returns the newest operation records (from 1 to 100); `GET /api/operations/{id}` returns one record or `404`. A record includes operation type (`Install`, `Update`, `Rollback`, or `SelfUpdate`), lifecycle status, durable step, versions, timestamps, and an optional error.

Records are JSON files in `Operations:StoragePath`, defaulting to `/var/lib/rustserver/updater/operations`. Each update is first flushed to a temporary file and then atomically replaced, so an accepted transition is not reported before it has been saved. A successful Prepare operation persists its verified staging result before transitioning to `Succeeded`. Corrupt individual files are ignored and logged without hiding other operation history.

Operations transition only from `Pending` to `Running`, `Failed`, or `Interrupted`, and from `Running` to `Succeeded`, `Failed`, or `Interrupted`. Failed and interrupted records are retained. On startup, incomplete ordinary operations become `Interrupted`; executor operations are instead reconciled from the executor-owned transaction and expose `deployment` with its confirmed active version and rollback state.

## GitHub authentication

Public repositories still require the configured credential when refresh is invoked, so availability diagnostics remain predictable. For private releases, initial provisioning may store the dedicated read-only Updater token at `/etc/rustserver/updater-github-token` with mode `0600`. The systemd unit reads it with `LoadCredential` and only supplies the service a private credential copy; the source token is masked from the service mount namespace.

The token is sent only as an HTTP Authorization header and is never written to application configuration, URLs, command lines, API responses, or logs. Prepare requests use the same provider; redirects are followed only over HTTPS and never receive the API Authorization header outside the configured GitHub API origin. If it is unavailable, local GET endpoints continue to work, while Refresh returns a non-secret `503` diagnostic and a Prepare operation records a non-secret failure.

### Runtime GitHub token ownership

The Updater token and the Plugins Git token are **two distinct service credentials**,
not GitHub Actions secrets:

| Runtime service | Host-side credential source | Intended GitHub access |
| --- | --- | --- |
| `rustserver-updater.service` | `/etc/rustserver/updater-github-token` | Read-only access to private GitHub Releases for registered applications |
| `rustserver-plugins.service` | `/etc/rustserver/plugins-github-token` | Read and write **only** to `sysdba925/RustServer.PluginFiles` and `sysdba925/RustServer.ConfigFiles` |

Updater already uses the first credential through systemd `LoadCredential`.
Its GitHub operations discover/download releases; it does **not** need repository
write permissions. Plugins' separate file and systemd wiring are a **planned
Bootstrap/Plugins integration**, not evidence that the service is already
using the second credential.

Provisioning must keep each host-side file root-owned and mode `0600`, and
configure each service to read its **own** file via `LoadCredential`.
Neither service reads the other service's credential source. The credential
must never be embedded in an origin URL, Git config, process arguments, logs,
or committed files.

For initial deployment, both files **may contain the same existing token
value**; this does not make the effective permissions read-only for Updater.
The real GitHub permissions follow the token value. In a later deployment,
the files can hold different fine-grained GitHub tokens: Updater with read-only
release access, Plugins with read/write repository contents permission
restricted to the two named repositories. This rotation must not require
application code changes.

The Plugins service owns its Git workspace initialization and remote Git
operations; Bootstrap only provides its credential, paths, permissions,
and service configuration (not Git clone/fetch/push). See the
[`RustServer.Plugins` README](https://github.com/sysdba925/RustServer.Plugins/blob/develop/README.md)
for the target design. Updating the existing Bootstrap workspace-sync behavior
is separate implementation work.

## Local development

```powershell
dotnet run --project src/RustServer.Updater.Api
dotnet test RustServer.Updater.slnx
```

## GitHub Releases

Visual Studio publish profiles package and upload Linux release artifacts through the locally authenticated GitHub CLI. See [the publishing guide](docs/publishing.md) for the release workflow, safeguards, and a safe dry-run command.


First-install failures retain up to 64 Ki characters from the end of each hook stream (stdout and stderr), with truncation and read errors indicated. Emergency Operation details and the operations API show this output and the exit code automatically. Secure the listener **and all reverse-proxy routes** for trusted administrators; the example platform configuration can listen on `0.0.0.0`, and Updater itself does not authenticate callers. Redaction covers common credential assignments and secret-valued executor environment variables, but cannot identify every secret: treat operation and deployment files, API responses and copied details as sensitive. The Updater state directory must remain accessible only to its service account and trusted administrators.
