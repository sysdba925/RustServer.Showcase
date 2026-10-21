# RustServer.Updater — DotNet + StaticWeb architecture

Status: **Approved for implementation planning** (12 October 2026). This document records the architecture agreed in chat; it is not a claim that implementation exists.

## Runtime configuration status

The authoritative Development and Production runtime-configuration contract is
[`runtime-configuration-layout.md`](runtime-configuration-layout.md). It is
approved but not yet implemented. References in this document to current
Bootstrap-owned configuration or existing source anchors must therefore be read
as pre-migration behavior; they do not authorize an additional configuration
location or an `/etc/rustserver/` subdirectory. See the companion
[`runtime-configuration-migration.md`](runtime-configuration-migration.md) for
the verified migration inventory.

## Scope and boundaries

One `RustServer.Updater`, one GitHub Releases / SHA-256 / staging / immutable versioned installation pipeline, one root-owned Deployment Executor and durable rollback/recovery workflow. Add two application types, `DotNet` and `StaticWeb`. Do not introduce `WebUpdater`, a second executor, microfrontends, or user-supplied privileged deployment commands.

`StaticWeb` means a **prebuilt** React/Vite `dist/` payload, served by Nginx. Neither Node nor .NET runtime is needed to *serve* its production files. Build and release publishing occur outside production deployment.

Nginx installation and the root-owned base configuration are owned by
`rust-server-platform`; the GitHub Web release contains only static files.
The base server exposes a root-owned application-snippet include point for
application-owned routes. A snippet must retain its own authorization boundary
and loopback-only upstream; the include point is not an authorization bypass
or a way to publish backend ports. Development's Vite server remains separate
from production.

The public `/` is a Platform-owned static landing page, installed by Production
Bootstrap independently of Web releases. Dashboard locations keep the Admin
auth subrequest and redirect its 401/403 responses to `/`; authorization service
errors remain errors and API responses are not redirected. Only the exact `/`
serves the landing page; unknown URLs and `/index.html` remain 404. The local
release-identity readiness route and atomic Web release switching are unchanged.

## Existing implementation anchors (develop branch reviewed 12 October 2026)

- `RustServer.Updater/src/RustServer.Updater.Api/Applications/ApplicationRegistration.cs`
- `ApplicationRegistrationIniParser.cs`, `ApplicationRegistrationValidation.cs`
- `PackagePreparationService.cs`, `PreparedPackageManifest.cs`
- `DeploymentApplicationHandler.cs`, `FileInstalledVersionProvider.cs`, `SystemdApplicationStatusProvider.cs`
- `RustServer.Updater/src/RustServer.Updater.Api/Deployments/*`
- `rust-server-platform/deploy/config/applications.ini`
- `RustServer.Updater/src/RustServer.Updater.Executor/*`
- `rust-server-platform/scripts/bootstrap.d/92-build-rustserver-web.sh` and `94-install-rustserver-web-service.sh` (development Vite)
- `RustServer.Web/vite.config.ts` (four development proxy prefixes).

At review time no `AGENTS.md` appeared in `RustServer.Updater` or `rust-server-platform`; `RustServer.Web/AGENTS.md` exists and requires backend-contract alignment and shared changelog discipline.

## Architectural decision

> Updater deploys immutable, versioned application artifacts. `DotNet` and `StaticWeb` are type-specific policies over the same package preparation, installation, activation, health and recovery lifecycle. The type changes payload validation and activation mechanics, not the trust boundary or public deployment pipeline.

Conceptual flow:

```text
GitHub Release + checksum
   -> safe download/extract/verification
   -> type-specific payload root validation
   -> immutable manifest and staged files
   -> root executor validates registration and exact inventory
   -> root-owned releases/<tag>
   -> type-specific pre-activation
   -> atomic current -> releases/<tag>
   -> type-specific post-activation/readiness
   -> success OR rollback + previous readiness verification
```

## Registration contract

Add an explicit `ApplicationType` (`DotNet`, `StaticWeb`). Prefer a single immutable `ApplicationRegistration` with type-dependent validation, not a subtype hierarchy. `Type` absent in legacy entries should be interpreted as `DotNet` **for backwards compatibility**; reject unknown values. Do not weaken strict unknown-key, path, duplicate or variable-substitution validation.

- `DotNet`: `Service` and a safe DLL `Entrypoint` required; `EntryFile` forbidden.
- `StaticWeb`: `EntryFile` (initially `index.html`) required; `Service` and DLL `Entrypoint` forbidden. Treat Nginx as platform infrastructure, **not** the per-app service.
- `Repository`, `Directory` and a trusted HTTP readiness URL remain universal.
- Permit a per-application `HealthPath` override, with the present `[Defaults] HealthPath=/health` retained for DotNet. `HealthPort` continues to be validated; use a locally bound Nginx listener for StaticWeb readiness.
- Protect unique names and install roots; enforce unique systemd units **only where a unit exists**.

Illustrative registration (proposal, not the installed file):

```ini
[Defaults]
RepositoryOwner=sysdba925
InstallRoot=/opt/rustserver
HealthScheme=http
HealthHost=127.0.0.1
HealthPath=/health

[Application:RustServer.Updater]
Type=DotNet
Repository=RustServer.Updater
Directory=updater
Service=rustserver-updater
Entrypoint=RustServer.Updater.Api.dll
HealthPort=8080

[Application:RustServer.Web]
Type=StaticWeb
Repository=RustServer.Web
Directory=web
EntryFile=index.html
HealthPort=80
HealthPath=/
```

**Open design choice for implementation:** reserving the readiness HTTP path `/` risks accepting another/default Nginx site's 200. Prefer a stable, local-only route or check body/response marker that identifies the active Web release, to ensure the probe cannot falsely pass. Keep the probe URL policy controlled by root-owned configuration. An Nginx listener on 127.0.0.1 and a separate public HTTPS virtual host are allowed. Exact ports/hosts must be chosen from deployment topology, not guessed.

## Common vs type-specific responsibilities

| Stage | Common | DotNet | StaticWeb |
|---|---|---|---|
| Release lookup and channel | Yes | — | — |
| Asset naming `<name>-<tag>-linux-x64.tar.gz` + `.sha256` | Yes for first iteration | — | — |
| Download, SHA-256, limits, safe extraction | Yes | — | — |
| Package-root validation | Dispatch | `.dll` | `index.html` / configured EntryFile |
| Full link-free inventory and staging | Yes | — | — |
| Copy and inventory recheck in root-owned releases | Yes | — | — |
| Stop/start runtime | Dispatch | `systemctl stop/start` | No operation |
| Switch `current` link | Yes, atomic | — | — |
| Runtime presence / readiness | Dispatch | `systemctl is-active` + HTTP | HTTP through Nginx only |
| Rollback and durable recovery | Shared transaction | Restore link, start/check unit | Restore link, recheck HTTP |
| Current installed version | Shared `current` symlink | — | — |

Use narrowly scoped branching/policies inside the existing fixed-function root executor. Never pass command, symlink target, entrypoint, service name or arbitrary path across the privilege boundary. The executor must re-parse the root-owned INI and independently validate the installed target and staging manifest. Keep the existing UUID-only Unix socket and recovery lock.

## Static release structure and serving

```text
/opt/rustserver/web/
  releases/
    v0.1.0/index.html
    v0.1.0/assets/...
    v0.2.0/index.html
    v0.2.0/assets/...
  current -> releases/v0.2.0
```

Nginx `root /opt/rustserver/web/current;` (illustrative) and SPA `try_files $uri $uri/ /index.html;`. `current` is replaced atomically; do **not** restart or reload Nginx per deployment. Protect against a missing or symlinked entry file and malformed release directories. Ensure web server read/traverse permissions and prevent unsafe writable exposure.

Existing Vite dev proxy prefixes should be preserved as same-origin production Nginx routes, with the prefixes stripped before forwarding:

```text
/control-api/*   -> Control   (currently loopback :5112)
/versions-api/*  -> Versions  (currently loopback :5113)
/plugins-api/*   -> Plugins   (currently loopback :5114)
/snapshots-api/* -> Snapshots (currently loopback :5115)
```

The cited ports reflect the current example topology, not a promise that production will use those values. Configure `proxy_pass` slash semantics precisely; test that `/control-api/api/server/status` becomes `/api/server/status`. Do not expose backend ports publicly. Validate SPA fallback does not intercept API failures. Avoid indiscriminate `index.html` caching; fingerprinted assets may be cached aggressively, but retain files referenced by existing clients over the transition period if necessary.

## Readiness, status, and rollback

StaticWeb readiness is an HTTP check **through Nginx**, proving the serving path, not just checking that `index.html` exists. It must identify the intended installed release to prevent a wrong virtual host / cached / stale page from yielding a false 200. Unavailability of unrelated backend APIs should **not** trigger Web rollback. Nginx configuration and upstream routing are checked separately as platform infrastructure.

Retain existing public application status enum initially (`NotInstalled`, `Running`, `Stopped`, `Unknown`) for compatibility. For `StaticWeb`: no valid `current` -> `NotInstalled`; successful HTTP readiness -> `Running` (legacy enum meaning 'serving'); failed or indeterminate readiness -> `Unknown`. For `DotNet` continue reading systemd state as before. Do not represent nginx's global service status as an individual Web app status.

After a static activation failure, restore the prior `current`, verify the restored serving path, and durably record whether rollback succeeded. Handle first installation (no prior version) without stopping Nginx; validate the resulting no-current state. Preserve previous releases and current transaction semantics.

## Security and compatibility invariants

1. Only the operation UUID crosses the process-to-root socket boundary; the root-owned INI remains sole deployment authority.
2. No client-controlled paths, service/unit names, health URLs or shell commands are executable by root.
3. INI validation is intentionally duplicated in C# and the executor's Python parser; keep them independently validating the same contract, with shared test cases.
4. Archive protection and manifest inventory / TOCTOU checks remain unchanged in strength.
5. `DotNet` behavior, existing registrations (including omitted `Type`), public endpoints, durable operations and boot-time recovery continue working.
6. No `npm`/Node/Vite or `.dll` validation is performed for the **StaticWeb** runtime.
7. Config and executor updates must be coordinated: deploy a backward-compatible Updater/executor before registering StaticWeb. Bootstrap is create-only for registry; changes need explicit reviewed migration.
8. Test interrupted transactions, malicious INI, changed staging, rollback failure, stale Nginx routes and false-positive readiness.

## Release build contract

`RustServer.Web` CI/publishing machine: `npm ci` -> checks/tests -> `npm run build` -> package `dist/` output as `<ApplicationName>-<tag>-linux-x64.tar.gz` and `.sha256` -> GitHub Release with channel-appropriate tag. Preserve the existing asset naming contract for now despite the platform-neutral static payload. The production host only downloads and serves the built files.

## Contract ownership

- `RustServer.Updater`: public endpoints/DTOs, package validation, preparation, orchestrating deployment admission, runtime status. Update its `docs/api-contract.md`, `openapi.json` and relevant documentation where a public contract changes.
- `rust-server-platform`: root executor, bootstrap, Nginx config/installation, global topology, platform tests, shared changelog.
- `RustServer.Web`: build/release publishing, deploy-time API base paths, and frontend behavior. Follow its `AGENTS.md` rules before changing code.

This design is approved at the conceptual level; exact Nginx health identity mechanism, production listener/hostname, migration order and backward-compatible JSON serialization must be resolved in the appropriate implementation stage before deployment.
