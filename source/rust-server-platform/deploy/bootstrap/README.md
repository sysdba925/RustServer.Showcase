# RustServer Updater Installer and Bootstrap

`deploy/install.sh` is the single production entry point for initial
provisioning of the prebuilt `RustServer.Updater` on Ubuntu 24.04 x64. It
downloads Bootstrap from the private `sysdba925/rust-server-platform` repository
at the `develop` branch; it never clones a Git repository or builds an
application from source.

Bootstrap installs ASP.NET Core Runtime 10, one verified bundled Updater
release, its API/Executor systemd resources, the production Nginx StaticWeb
route, SSH terminal utilities (Step 22), and the create-only prerequisites for
`RustServer.Auth`. It does not install the .NET SDK, Node.js, Vite, or activate
any additional RustServer application service without an explicit Updater operation.

> **Configuration status:** Production Bootstrap provisions the runtime layout
> in
> [`docs/architecture/runtime-configuration-layout.md`](../../docs/architecture/runtime-configuration-layout.md).

## Run the Installer

Download `deploy/install.sh` from a trusted copy of this repository and run it
as a normal sudo-capable user. Do not run production commands from this README
on a development machine.

```bash
bash ./install.sh
```

The default `install` mode is for an initial installation and safe
infrastructure recovery. It verifies Ubuntu 24.04 x64, downloads a temporary
Bootstrap archive, then runs its numbered steps:

1. Check host prerequisites.
2. Install or verify ASP.NET Core Runtime 10.
3. Install missing SSH terminal utilities and one-time `tools/help/info` help.
4. Create missing directories and listener configuration.
5. Select `Development` or `Production` and find the highest matching published
   Updater release before any Updater archive is downloaded.
6. Download, verify, and install the initial complete Updater bundle (API,
   Executor, manifest, service, socket, and recovery resources) if there is no
   active release.
7. Create the runtime registry, release-channel setting, and credential on a
   first installation.
8. Install the service, socket, socket worker, and recovery units from that
   same active bundle, then create the stable runtime drop-ins.
9. Enable the Executor socket and start `rustserver-updater.service`; its
   recovery dependency runs before the API starts.
10. Provision the Auth identity, immutable release and persistent data
   directories, and `rustserver-auth.service` without starting it.
11. Install or reconcile the root-owned Nginx StaticWeb configuration.
12. Verify that `/health` returns HTTP 200.

### Terminal tools (Step 22)

Production Step 22 is **self-contained**. Its installer and static help catalog
are stored exclusively under `deploy/bootstrap/`:

```text
deploy/bootstrap/
├── steps/22-install-terminal-tools.sh
├── assets/tools
└── lib/common.sh
```

It does not load, invoke, import, or depend on Development Bootstrap or
anything under `scripts/`. The Production Installer downloads the whole
platform archive, but the terminal toolkit uses only the production paths
shown above.

The step installs missing SSH utilities (TermIDE, `mc`, `lazygit`,
`lazydocker`, `micro`, `btop`, `tmux`, `ncdu`, `fzf`, `rg`, `jq`,
`duf`, `bat`, `dust`, `lnav`, `yazi`, Helix, `systemctl-tui`,
`lazyjournal` and Posting). Non-APT executables are installed from
SHA-256-verified GitHub release assets; Posting uses user-local pipx.

Before sudo/APT/network activity, Step 22 checks whether all tools, the
`tools` command, and `help/info` shell functions are already configured.
When complete, it immediately logs `SKIP`. Otherwise it installs only the
missing tools and adds the Bash help integration only if it is absent.

`deploy/cleanup.sh` preserves the installed programs, the `tools`
command, and `~/.bashrc`. No separate terminal setup is needed after
cleanup. Open a fresh SSH session and run `tools`, `help`, or `info`
for a concise Russian-language reference. `tools` lists only commands
that are available on the host; it has no `--all` mode.

Existing directories, configuration files, credentials, persistent state,
installed release directories, and the `current` link are preserved.
In particular, running `install` again never automatically downloads or
activates a newer Updater release.

The installed Rust Docker container runs as SteamCMD UID 1000. Production
Bootstrap grants only that UID read access to the canonical
`/etc/rustserver/rcon-password` through an ACL for the Compose secret mount;
root remains the owner, and ordinary group/other access remains disabled.
The resulting file mode is `0640` because of the ACL mask. Repeated Bootstrap
runs validate this exact ACL without replacing or exposing the password.

## NetworkAccess host boundary

Production Bootstrap prepares, but does not implement, NetworkAccess rule
logic. The `RustServer.Control` release supplies the root-owned helper DLL and
its unit at:

```text
/opt/rustserver/control/current/network-access/systemd/rustserver-network-access-helper.service
```

Bootstrap installs `nftables`, `iproute2` (`ss`), and `kmod` when needed,
loads `nf_tables` now, and creates the root-owned module-load declaration
`/etc/modules-load.d/rustserver-network-access.conf` for future boots. It does
not enable `nftables.service` or write `/etc/nftables.conf`: that could replace
an administrator's firewall policy. Before Docker starts, the release unit
runs its fixed release-owned fail-closed guard for only `28015/udp` and
`28017/udp`; a successful helper apply replaces it with the persisted mode and
maintenance DNAT. The helper owns this named nftables table and is the only
RustServer component that changes its rules.

Bootstrap does not install, link, or enable the NetworkAccess helper: its
binaries and unit belong to the Control release. During Control deployment the
root-owned Updater Executor verifies the release helper assets, installs the
stable systemd unit link, runs daemon-reload, and starts Control with its normal
systemd dependency on the helper. Once Control passes readiness, the Executor
enables both services for boot. On first-install failure, it removes the
helper registration during rollback. No separate Helper release is required.

Control itself remains `User=rustserver-control` with
`NoNewPrivileges=true`; it receives no sudo rule and no root command path.
The helper socket directory is created by its release unit as
`root:rustserver-control` with mode `0750`. Bootstrap rejects a Control
release tree that is not entirely root-owned or is writable by group or
other users.

`rustserver-control.service` is `PartOf=docker.service`. A Docker restart
therefore restarts Control after Docker and the helper; Control startup
reconciliation reapplies the persisted NetworkAccess settings. A reconciliation
failure caused by Docker's container-start race is retried every 15 seconds.
The helper discovers the running Compose `rust-server` container's current
IPv4 address for every apply, so a recreated container is never reached
through a stale maintenance DNAT target. The helper fails closed when Docker
does not expose the expected `DOCKER-USER` nftables chain, or when there is
not exactly one running Rust container with one IPv4 address.

After a Control release containing NetworkAccess has been activated, verify
the host boundary before using the API:

```bash
sudo systemctl daemon-reload
sudo systemctl restart rustserver-control.service
sudo systemctl status rustserver-network-access-helper.service --no-pager
sudo stat -c '%U:%G:%a %n' /run/rustserver-network-access
sudo nft list chain ip filter DOCKER-USER
```

The last command is expected only after Docker has created its iptables-
compatible `DOCKER-USER` chain. Docker's native nftables firewall backend and
custom layouts without that chain are intentionally unsupported.

Bootstrap launches each numbered step in a separate Bash process. On a clean
installation, step 45 therefore writes the selected channel and exact
validated tag to a root-owned, mode-`0600` volatile hand-off under `/run`; step
50 consumes it, revalidates the release metadata and assets, and removes it
only after activating `current`. The durable `/etc/rustserver/updater-release-channel`
file stores only the operator's channel selection.

## RustServer.Auth provisioning and activation

Bootstrap registers `RustServer.Auth` in the shared application registry with
its published `RustServer.Auth-v<version>-linux-x64.tar.gz` artifact convention,
`RustServer.Auth.dll` entrypoint, and loopback readiness endpoint
`http://127.0.0.1:5286/health/ready`. It creates, when absent:

```text
/opt/rustserver/auth/releases/             # root-owned immutable releases
/var/lib/rustserver/auth/                   # rustserver-auth persistent state
/etc/systemd/system/rustserver-auth.service # reviewed production unit
```

`/etc/rustserver/auth.env` is the sole persistent Auth environment file.
On a first interactive installation, the Production Installer collects the
Google and first-admin values, stores them only in its mode-`0600` temporary
workspace, and passes the temporary file path to Bootstrap. Bootstrap validates
the handoff and atomically creates `auth.env` as `root:rustserver-auth`, mode
`0640`. The temporary handoff is removed by the Installer on success, error, or
handled interruption. systemd reads the canonical file through the Auth unit's
`EnvironmentFile=` directive and passes the values to the service.

The file must define non-empty `GOOGLE_CLIENT_ID` and
`GOOGLE_CLIENT_SECRET`. For the first start of an absent or empty Auth database,
also provide `ADMIN_USERNAME` and `ADMIN_PASSWORD`; remove the initial password
after the application has created the administrator. `Bootstrap__AdminUserName`
is optional but, when used for first initialization, must match
`ADMIN_USERNAME`. Do not place these values in a systemd unit, a release
directory, or another persistent Auth environment file.

On a reinstall, an existing valid `auth.env` remains authoritative and is
preserved byte-for-byte; the Installer does not request Auth credentials again.
An existing file that is unsafe, has the wrong `root:rustserver-auth`/`0640`
contract, or lacks required values causes a clear failure and is never replaced
automatically. Bootstrap only reloads systemd metadata for the unit; it never
enables or starts Auth itself. The Deployment Executor consumes and validates
the canonical file before activation, but never collects or persists Auth
credentials. A successful, ready first installation is then enabled for future
boots.

Use the normal explicit sequence after Installer provisioning is complete:

```text
Prepare RustServer.Auth v<version> -> Install RustServer.Auth v<version>
```

The Auth SQLite file remains at `/var/lib/rustserver/auth/auth.db`; neither
Bootstrap nor the executor places it under a release directory or deletes,
replaces, migrates backward, or restores it. A failed binary activation first
tries the prior release and requires its readiness probe to succeed against the
current database. Therefore, binary rollback is only safe when that prior
release supports the database schema already present; operators must plan and
test forward EF migrations accordingly. A rollback that cannot become ready is
reported as failed rather than claiming that data was restored.

## Plugins GitHub credential

Production Bootstrap maintains a **separate** Plugins GitHub credential:

- Host file: `/etc/rustserver/plugins-github-token` (`root:root`, `0600`).
- Runtime service: `rustserver-plugins.service`, using a systemd
  `LoadCredential=github-token:/etc/rustserver/plugins-github-token`
  drop-in and `RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE=%d/github-token`.
- GitHub scope: Contents **read/write** for **only**
  `sysdba925/RustServer.PluginFiles` and
  `sysdba925/RustServer.ConfigFiles`.

On the **first** install, the Installer requires a dedicated **Plugins Token**
via the `GitHub plugins token:` prompt, `PLUGINS_GITHUB_TOKEN`, or
`PLUGINS_GITHUB_TOKEN_FILE` (mode `0600`). There is **no fallback to the
Updater Token**. If the Plugins credential is absent, installation stops before
provisioning it. Repeated installs preserve and validate the existing Plugins
file without prompting or replacing it. Rotation is a separate explicit
operator action.

Create three separate fine-grained GitHub credentials:
- **Bootstrap Token**: Contents read access to only
  `sysdba925/rust-server-platform` and `sysdba925/RustServer.Updater`.
- **Updater Token**: Contents read access to the repositories of **all**
  applications whose GitHub Releases the Updater discovers or downloads,
  including `RustServer.Updater`.
- **Plugins Token**: Contents read/write access to **only**
  `sysdba925/RustServer.PluginFiles` and
  `sysdba925/RustServer.ConfigFiles`.
These permissions are configured in GitHub; Bootstrap does not grant or
verify repository-level GitHub permissions.

The credential is never added to the application release package, Git
configuration, systemd environment values or logs. The service reads its
systemd-provided protected copy.

Production Bootstrap prepares the empty workspaces; the **Plugins service**
owns first-start Clone into them. The service starts that initialization
only when the Rust server is stopped and the shared Git mutation guard is
available. Workspace initialization must not rewrite existing Git trees.

**Current design:** Development Bootstrap no longer manages the Git
workspaces of PluginFiles/ConfigFiles and does not require pre-cloned
repositories. On a clean deployment, Plugins creates its own checkouts from
the canonical HTTPS repositories. Bootstrap still uses Git/SSH for other
service source workflows where applicable.

## Production StaticWeb serving

Nginx owns the production public routing for `RustServer.Auth` and
`RustServer.Web`. It serves the active Web release through
`/opt/rustserver/web/current`; Node.js, Vite, and a separate frontend server
are not present on the production host. The public contract is deliberately
small:

| Public path | Destination | Access policy |
| --- | --- | --- |
| `/` | Platform landing page at `/opt/rustserver/landing/index.html` | Public; LOGIN links to `/login` |
| `/login`, `/login.css`, `/login.js`, `/admin`, `/portal` | Auth at `127.0.0.1:5286`, with the original URI | Auth endpoint policy |
| `/api/auth/*`, `/signin-google`, `/signin-steam` | Auth at `127.0.0.1:5286`, with the original URI | Auth endpoint policy |
| `/dashboard`, `/dashboard/*` | Active Web release | Authenticated user with Auth's `Admin` role |
| `/dashboard/assets/*` | `current/assets/*` | Authenticated user with Auth's `Admin` role |
| `/control-api/*`, `/versions-api/*`, `/plugins-api/*`, `/snapshots-api/*` | Platform-managed loopback API | Authenticated user with Auth's `Admin` role |
| Application-owned routes, including `/plugin-catalog-api/*` after Catalog host provisioning | Reviewed root-owned application snippet and its loopback API | Snippet's explicit authorization policy; Catalog requires Auth's `Admin` role |
| `/index.html`, `/player`, and unknown paths | — | `404` |

The `/dashboard` SPA fallback exists only within the Dashboard route; it never
turns an unknown public path or a missing asset into `index.html`. The legacy
root `/assets/*` path is not public. `index.html` is deliberately not an
entrypoint or immutable-cached response.

Nginx uses Auth's existing `GET /api/auth/authorize/admin` endpoint through
an internal `auth_request` subrequest before it serves the Dashboard or proxies
any backend API. The subrequest forwards the browser's Identity cookie to Auth;
Auth returns `200` for an administrator, `401` for no valid session, and `403`
for a non-administrator. `/api/auth/verify` remains available for a future
authenticated-but-not-admin route, but is not sufficient for this
administrator-only surface. This proxy check complements, rather than
replaces, authorization in the backend services; all backend listeners must
remain loopback-only.

Dashboard requests receiving 401 or 403 from this check redirect to `/` with
HTTP 302. Auth service failures remain errors; API authorization responses
keep their status. The landing page is independent of Web releases and its
readiness check. Bootstrap creates it as `root:root` mode `0644` in a
`root:root` mode `0755` directory. A matching file is preserved on rerun;
conflicting content, links, or unsafe ownership and modes stop Bootstrap for
operator review. Back up or move a conflicting file before rerunning.

This routing treats the current Platform-managed backend prefixes as one
administrative control-plane surface. Application-owned snippets must preserve
the same explicit authorization decision. If any endpoint later needs Player access, machine-to-
machine access, or a different permission, agree and document that endpoint-
level policy before adding a public exception; none is inferred from the
presence of a hidden UI route.

Auth routes deliberately use `proxy_pass` without a URI component, preserving
the original request URI and query string. Nginx passes `Host`,
`X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host` to Auth, which
is required for its trusted-proxy and OAuth callback handling.

The browser-facing listener is port `80`. API paths retain the Vite
development semantics and strip their public prefix before forwarding only to
loopback backends; their existing URI transformations are unchanged:

```text
/control-api/*   -> 127.0.0.1:5112/*
/versions-api/*  -> 127.0.0.1:5113/*
/plugins-api/*   -> 127.0.0.1:5114/*
/snapshots-api/* -> 127.0.0.1:5115/*
```

The base server includes root-owned regular snippets from
`/etc/rustserver/nginx/applications.d/*.conf`; Bootstrap creates that directory
as `root:root` mode `0750`. Service identities cannot write it. This is the
only extension point for application-owned routes: a snippet must declare its
own Admin `auth_request` and loopback upstream. Platform does not carry a
Catalog route or Catalog provider configuration.

The deployment readiness probe is intentionally separate from the public
listener: `http://127.0.0.1:8081/_rustserver/web-readiness`. It returns `200`
only when `current` resolves directly to a valid versioned release directory
containing `index.html`, and returns exactly one `X-RustServer-Release` header
whose value is the resolved release directory name. The route derives that
value on each request from Nginx's real path resolution, so changing `current`
does not reload or restart Nginx. With no active Web release, Nginx remains
healthy but this readiness route returns `404`.

Bootstrap creates `/opt/rustserver/web/releases` as root-owned and
world-traversable; the Deployment Executor creates immutable root-owned,
readable release trees below it. Bootstrap validates a candidate configuration
with `nginx -t` before installation, validates the complete installed
configuration again, restores the previous managed configuration if the final
validation fails, and reloads Nginx only when that configuration changes.
It never reloads or restarts Nginx during ordinary Updater-driven Web release
activation.

Clean Production Bootstrap configures Updater and the platform topology for loopback listeners. Auth receives an explicit loopback URL in its newly created systemd unit. Rcon uses the two loopback URLs in topology.env. The Rust Compose file publishes WebRCON on loopback while leaving game UDP ports available; Bootstrap does not change SSH or firewall policy. Application releases must honor their listener configuration and application-owned provisioning must keep its APIs on loopback. Verify actual listeners with `sudo ss -lntup` after each installation, including application-owned services.

### HTTPS and Web build prerequisites

The committed Nginx configuration intentionally listens only on HTTP port 80;
it does not invent a certificate, hostname, or TLS policy. It is therefore a
routing contract, not a complete public production exposure. Before exposing
login or OAuth publicly, provision a real HTTPS listener for the approved
hostname, configure the provider redirect URIs to its exact
`https://<host>/signin-google` and `https://<host>/signin-steam` callback
paths, and ensure the TLS terminator passes the original HTTPS host and scheme
to this loopback Nginx/Auth boundary. Auth uses `Secure` cookies, so browsers
will not return a session cookie over plain HTTP; OAuth callback generation
also depends on trusted `X-Forwarded-Proto=https` and
`X-Forwarded-Host=<public host>`. The TLS vhost or trusted upstream-proxy
design is an explicit operator decision and is not created by Bootstrap.

The current `RustServer.Web` source is not yet deployable below `/dashboard`:
its Vite configuration has no `/dashboard/` `base`, and its React Router has
no `/dashboard` `basename`. Update that repository to build root-relative
assets as `/dashboard/assets/*` and to route within `/dashboard`, then publish
a new static release. Nginx intentionally does not rewrite a root-based build
to hide this incompatibility.

## Updater lifecycle

Bootstrap provisions the first complete Updater bundle only. All subsequent
self-update, rollback, deployment locking, and recovery are owned exclusively
by the bundled `RustServer.Updater.Executor`; Bootstrap is not a runtime
dependency.

## Deployment Executor

The ASP.NET Core service runs as `rustserver-updater` and cannot run arbitrary
root commands. It writes a durable UUID request then sends that UUID over the
group-restricted `/run/rustserver-deployment-executor.sock`. systemd socket
activation starts a root-owned executor service for that connection, so an
Updater self-update can stop `rustserver-updater.service` without interrupting
the release switch, health check, or rollback. The Updater has no sudo rule;
`NoNewPrivileges=true` and its other systemd hardening remain enabled.

The executor accepts only an operation UUID and reads its request and matching
operation record from fixed state paths. Immediately before every privileged
operation it parses the same root-owned `/etc/rustserver/updater/applications.ini`
as the Updater. That one registry fixes the installation root, type-specific
service unit or entry file, repository, and readiness URL; HTTP request and
mutable staging values are never trusted as authority. Legacy registrations
without `Type` are DotNet. StaticWeb requires `EntryFile=index.html` and has no
application systemd unit.

Stage 6 manifests include the complete link-free package tree (path, type,
size, and SHA-256 for every file). Before installing, the root worker validates
staging and writes that inventory into root-owned release storage; it verifies
the copied tree against the snapshot before `current` can change. A modified,
added, removed, or linked staging entry therefore cannot be substituted between
verification and installation.

Staging packages created before this inventory was added are deliberately
rejected. Run Prepare again for the exact release before requesting deployment.

DotNet activation keeps its systemd stop/start/active-unit and HTTP readiness
checks. StaticWeb activation only validates the immutable `index.html`, switches
`current`, and probes the configured HTTP endpoint; it never queries, reloads,
or restarts Nginx or an application unit. To reject a default vhost, stale page,
or cache response, StaticWeb requires HTTP 200 and exactly one
`X-RustServer-Release` response header equal to the selected release tag. Stage
04 owns the Nginx route that supplies this proof. Until that route exists, a
StaticWeb activation safely rolls back and the Updater reports it as `Unknown`.
Rollback and boot-time recovery use the same type-specific policy.

`rustserver-updater-recovery.service` runs the Executor from the active bundle
before the Updater API starts and rolls back incomplete executor transactions.
It uses the Executor deployment lock; Bootstrap does not participate after the
first bundled release is active.

## GitHub authentication

The initial Installer download uses a **Bootstrap Token**. It uses
`GITHUB_TOKEN` when it is available, otherwise `GITHUB_TOKEN_FILE`, and
otherwise requests `GitHub bootstrap token:`. Secret prompts render one `*` per
entered character, including pasted input, and Backspace removes one `*`; the
value itself is never rendered. The Bootstrap Token is temporary and is never
saved in the installed configuration.

When `/etc/rustserver/updater-github-token` is absent, the Installer collects
a separate read-only **Updater Token**. It then collects a third, dedicated
read/write **Plugins Token** when `/etc/rustserver/plugins-github-token` is
absent, before asking for Google OAuth and admin credentials. On a repeated
installation it preserves and validates existing persistent credentials
without prompting or replacing them. `UPDATER_GITHUB_TOKEN` /
`UPDATER_GITHUB_TOKEN_FILE` and `PLUGINS_GITHUB_TOKEN` /
`PLUGINS_GITHUB_TOKEN_FILE` support non-interactive use:

```bash
GITHUB_TOKEN=github_pat_example bash ./install.sh
```

```bash
install -m 0600 /dev/null "$HOME/.config/rustserver/github-token"
# Write the token to the file without placing it in shell history.
GITHUB_TOKEN_FILE="$HOME/.config/rustserver/bootstrap-token" \
  UPDATER_GITHUB_TOKEN_FILE="$HOME/.config/rustserver/updater-token" \
  PLUGINS_GITHUB_TOKEN_FILE="$HOME/.config/rustserver/plugins-token" \
  bash ./install.sh
```

The Installer rejects token files readable by group or other users. It copies
the Bootstrap, Updater and Plugins tokens into separate restrictive temporary
files as needed, uses temporary curl configuration files instead of command-line
headers, and removes temporary files on success, error, or handled interruption.
Bootstrap writes the Updater and Plugins tokens only to their respective
root-owned credential files. Each service receives only its own token through
systemd `LoadCredential`; the source credential files are masked from the
unprivileged services. No token is written to persistent application
configuration, logs, URLs, or shell arguments.

Before downloading an Updater archive on the first install, Bootstrap asks for
`Development` or `Production` unless `UPDATER_RELEASE_CHANNEL` is explicitly
provided. It queries the private GitHub Releases API for
`sysdba925/RustServer.Updater`, excludes drafts, and selects the maximum SemVer
version across every result page—not the most recently published release.
Development accepts only `vX.Y.Z-dev.N` releases with `prerelease=true`;
Production accepts only `vX.Y.Z` releases with `prerelease=false`. If the
chosen channel has no matching release, installation fails with that channel in
the error and does not fall back to the other channel. The choice is persisted
only after a matching release is found and is shared by every registered
application.

## Persistent configuration and layout

For the runtime configuration contract, see
[`runtime-configuration-layout.md`](../../docs/architecture/runtime-configuration-layout.md).

Production Bootstrap reads installation defaults from the version-controlled
`deploy/bootstrap/bootstrap.env.example` included in the downloaded Bootstrap
archive. It parses the file as data on every Bootstrap invocation; it neither
creates nor requires a persistent Bootstrap configuration file. The defaults
are:

```dotenv
RUSTSERVER_UPDATER_BIND_ADDRESS=127.0.0.1
RUSTSERVER_UPDATER_PORT=8080
```

For the current Bootstrap invocation, the same-named environment variables
(`RUSTSERVER_UPDATER_BIND_ADDRESS`, `RUSTSERVER_UPDATER_PORT`, and
`GITHUB_TOKEN_FILE`) override those defaults.
They are used only when Bootstrap creates missing installation state; existing
runtime configuration and the current release remain unchanged. The Installer
supplies its bootstrap-token, Updater-token, and release channel environment
inputs. A new Updater environment rejects a non-loopback bind override. A clean installation has no fixed Updater tag in these defaults: the
selected channel determines the matching published archive and checksum.

`/etc/rustserver/updater.env` holds `RUSTSERVER_UPDATER_BIND_ADDRESS` and
`RUSTSERVER_UPDATER_PORT`; it is also create-only. Change listener settings
there directly and restart the Updater only when intentionally changing its
runtime configuration. Existing topology files and systemd units are also preserved. For an existing deployment, back up and explicitly migrate any non-loopback listener configuration or unit, then restart the affected service and verify its listeners before public exposure.

## Unified application registry

Bootstrap installs `deploy/config/applications.ini` as
`/etc/rustserver/updater/applications.ini`. The initial file contains the
currently supported application registrations, including the prepared
`RustServer.Web` `StaticWeb` registration on its local Nginx readiness port
`8081`. It is owned by
`root:rustserver-updater` with mode `0640`; the shared `/etc/rustserver`
directory is `root:root` with mode `0755` so independent Development and
Production service accounts can traverse it. Updater access remains limited by
the registry's group-read mode, while secrets remain individually protected.
Re-running `install` preserves the canonical file.

On an existing INI-based installation, the ordinary `install` recovery mode
performs narrowly scoped migrations when reviewed missing registrations are
required. It appends only the Auth or PluginCatalog section, preserves every
existing byte of the prior registry, writes a timestamped backup beside it,
and refuses a malformed registry or conflicts of application name, directory,
service name, or PluginCatalog health port.

## PluginCatalog release boundary

Production Bootstrap retains only the root-owned Updater registration for
`RustServer.PluginCatalog`, the common `/opt/rustserver` root and the protected
Nginx application-snippet include point. It neither installs PostgreSQL nor
creates a Catalog role/database/schema, media root, service user, Catalog
systemd unit, Catalog configuration or Catalog Nginx route.

Initial Catalog installation is deliberately not part of automatic emergency
Rollout. The operator must follow `Bootstrap → Updater Prepare → explicit
Catalog host/database/media provisioning → Updater Install`. Catalog-owned
provisioning runs from the verified prepared artifact and owns provider choice,
connection strings, schema, media root, systemd unit and the Admin-gated
`/plugin-catalog-api/` snippet. Update and Rollback do not provision anything.

For an already partially installed host, Bootstrap leaves existing Catalog
PostgreSQL data, media, configuration and unit untouched. The Catalog host
tool performs the documented conservative handoff: it accepts only the exact
known legacy Platform unit, saves a root-owned timestamped copy and otherwise
requires manual review. See
[`plugin-catalog-provisioning-boundary.md`](../../docs/architecture/plugin-catalog-provisioning-boundary.md).

To add a service that already has a reviewed Linux x64 release, use SSH and
edit the root-owned file. Do not add a registry entry until its release and
systemd unit are ready:

```bash
ssh administrator@server
sudoedit /etc/rustserver/updater/applications.ini
```

```ini
[Application:<ApplicationName>]
Repository=<RepositoryName>
Directory=<single-directory-segment>
Service=<systemd-service-without-.service>
Entrypoint=<safe-entrypoint.dll>
HealthPort=<1-65535>
```

Keep the shared `[Defaults]` values valid. The entry authorizes discovery and
deployment only; it does **not** download, install, start, or publish the
application. Call `POST /api/applications/refresh` to validate and reload the
change, then use Prepare and Install as separate explicit operations.

```text
/opt/rustserver/updater/releases/<release-tag>/  # immutable installed releases
/opt/rustserver/updater/current -> releases/<release-tag>
/opt/rustserver/web/releases/<release-tag>/      # immutable StaticWeb releases
/opt/rustserver/web/current -> releases/<release-tag>
/opt/rustserver/landing/index.html              # Platform-owned public landing page
/opt/rustserver/auth/releases/<release-tag>/     # immutable Auth releases
/opt/rustserver/auth/current -> releases/<release-tag>
/etc/rustserver/updater.env                      # permanent listener configuration
/etc/rustserver/updater/applications.ini          # root-owned unified allowed-application registry
/etc/rustserver/updater-release-channel           # persistent Development or Production selection
/etc/rustserver/updater-github-token              # root-owned Updater GitHub credential
/etc/rustserver/auth.env                          # Installer-provisioned canonical Auth environment
/etc/systemd/system/rustserver-updater.service    # API unit copied from the active bundle
/etc/systemd/system/rustserver-auth.service       # Auth production unit
/etc/systemd/system/rustserver-updater.service.d/runtime.conf # runtime API entrypoint and recovery dependency
/etc/systemd/system/rustserver-updater.service.d/credentials.conf # systemd credential sandboxing
/etc/systemd/system/rustserver-updater-executor.socket # group-restricted privileged handoff
/etc/systemd/system/rustserver-updater-executor@.service # root socket worker
/etc/systemd/system/rustserver-updater-recovery.service # bundled boot-time transaction recovery
/etc/nginx/conf.d/rustserver-web.conf             # root-owned production StaticWeb route
/run/rustserver-deployment-executor.sock          # service-owned UUID handoff socket
/var/lib/rustserver/updater/deployment-requests/  # service-owned UUID requests
/var/lib/rustserver/updater/deployments/           # root-owned transaction outcomes
/var/lib/rustserver/updater/                      # persistent Updater state
/var/lib/rustserver/auth/auth.db                  # persistent Auth SQLite database
```

Check the active service after an installation or upgrade:

```bash
sudo systemctl status rustserver-updater --no-pager
sudo journalctl -u rustserver-updater --no-pager -n 100
curl --noproxy '*' --fail http://127.0.0.1:8080/health
```

Use the address and port from `/etc/rustserver/updater.env` if they differ from
the defaults.

## Local maintainer checks

The tests use temporary files, fake Bootstrap steps, and mocked service
operations. They never contact GitHub, invoke production steps, or run the
Installer against the current VM.

```bash
bash ./deploy/tests/install-tests.sh
bash ./deploy/bootstrap/tests/bootstrap-tests.sh
shellcheck deploy/install.sh deploy/tests/*.sh \
  deploy/bootstrap/bootstrap.sh deploy/bootstrap/lib/common.sh \
  deploy/bootstrap/steps/*.sh deploy/bootstrap/tests/*.sh
```

## Production service provisioning

The Installer runs `deploy/bootstrap/provision-services.sh` after the
existing numbered Bootstrap steps. The same script can run standalone from
a full source checkout:

```bash
sudo bash deploy/bootstrap/provision-services.sh
```

It reuses `docker/rust-server/.env.example` for an absent
`/etc/rustserver/rust-server.env`, and uses the existing Development
`scripts/bootstrap.d/82-install-runtime-assets.sh` to copy Rust, SteamCMD
and Oxide Compose assets into `/opt/rustserver/runtime/`. Docker Engine,
Compose, Git and ACL prerequisites are installed only if missing.
Four dedicated service accounts, application state directories and
`deploy/systemd/` units are provisioned without starting application services.

Existing settings, credentials, application releases and service units are
preserved. A conflicting pre-existing service unit causes a clear error
instead of being silently replaced. The canonical topology and RCON password
must be created beforehand by existing Rcon Bootstrap step 73.

Game-data directory ACLs allow the current SteamCMD image's UID 1000 and the
Plugins/Snapshots service users access; existing data files are not
recursively altered. Plugins/Configs private Git workspace **directories** are prepared by
Bootstrap; RustServer.Plugins creates the actual Git checkouts through
its private systemd GitHub credential when first activated. Bootstrap
does not clone or synchronize either source repository.
**Only Updater installs and activates application releases.**

## Cleanup on a dedicated test host

`deploy/cleanup.sh` removes RustServer state, service identities and integration,
including the Catalog PostgreSQL database and role on each local cluster, the
NetworkAccess nftables objects, and managed Nginx configuration. It preserves
PostgreSQL and other databases and fails if a local cluster is offline or the
role still owns other objects. It restores the distribution default Nginx site
when a managed RustServer site indicates Bootstrap disabled it, validates the
configuration and reloads an active Nginx. Unnamed temporary Installer files
and user projects are not identified as RustServer-owned and are preserved.
Docker cleanup remains host-wide and destructive; see `deploy/HOW_TO_INSTALL.md`.
Cleanup logs each operation and reports the current stage, line and exit code on failure. It disables the systemd pager so unit listings do not wait for terminal input.
A failed operation or verification exits without the DONE message; correct the
reported problem and rerun the script. Do not run it on a shared host.
