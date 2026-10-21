# Ubuntu bootstrap

`scripts/bootstrap.sh` prepares a fresh Ubuntu 24.04 development or server VM for this platform. It is intentionally an ordered, fail-fast bootstrap: it validates the host, installs the development tools, creates the shared workspace layout, safely creates or migrates canonical Rust settings and Control state, installs the Docker Compose runtime assets, synchronizes the dependent repositories, builds them, installs and starts the Rcon gRPC, Control, Versions, Plugins, Snapshots, and Web systemd services, and then verifies the result.

It does not provision a VM, install Git, create SSH keys, configure GitHub, install Rust Dedicated Server content, or start the Rust server. Steps 85, 86, 93, 94, 95, and 97 install, enable, start, and validate their respective services.

> **Configuration status:** Stages 1 and 2 make
> `/etc/rustserver/rust-server.env` and `/etc/rustserver/rcon-password` the
> active Rust Dedicated Server settings and RCON secret. Bootstrap safely
> creates or migrates both; other runtime-configuration migrations remain
> separate work.

## Prerequisites

Before invoking bootstrap, use Ubuntu 24.04 LTS and make sure the following are already true:

- Git and the OpenSSH client are installed.
- GitHub SSH access is configured for the current user and can access the platform and dependent repositories.
- `rust-server-platform` has been cloned and its `origin` remote is configured.
- The user can authenticate with `sudo`.

Bootstrap does not create or store private keys and does not alter GitHub authentication. Step 10 verifies `bash`, `git`, `ssh`, and `sudo`, and confirms that the platform is a Git worktree with an origin. Repository-access failures are reported by their corresponding sync steps.

From the root of the manually cloned platform repository, run the one entry point:

```bash
./scripts/bootstrap.sh
```

The scripts determine their own physical location, so the command also works when invoked through its absolute path from another current directory.

## Plugins credential and source workspaces

Development Bootstrap can independently provision
`/etc/rustserver/plugins-github-token` (`root:root`, mode `0600`).
On a clean VM, set `RUSTSERVER_BOOTSTRAP_PLUGINS_GITHUB_TOKEN_FILE` to
a private token file before running `scripts/bootstrap.sh`. If a
protected Updater token already exists at
`/etc/rustserver/updater-github-token`, Bootstrap can instead copy
its value into the separate Plugins file. Existing Plugins credentials
are validated and never overwritten. **A token is required** on a
fresh VM: Bootstrap now fails with an explicit diagnostic instead of
attempting an SSH clone of the source repositories.

The generated Plugins systemd unit uses `LoadCredential` and
`RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE=%d/github-token`. It enables
`PluginManagement__WorkspaceInitialization__Enabled=true` so the
service creates and clones any missing `ConfigFiles` and `PluginFiles`
workspaces via authenticated HTTPS. Bootstrap **does not** clone, fetch,
push, stop the service to sync source workspaces, or inspect their
`.git` directories. Other microservice source checkouts still use the
separate existing Bootstrap Git/SSH sync workflow.

A copied token has its original permissions; Plugins requires real
GitHub Contents read/write access to PluginFiles and ConfigFiles for Push.
The two host files remain independently replaceable.

## Canonical workspace

Each numbered step derives the platform directory from its own physical location and derives `WORKSPACE_ROOT` as that directory's parent. The resulting layout is:

```text
<WORKSPACE_ROOT>/
├── rust-server-platform/           # PLATFORM_DIR; manually cloned first
├── oxide/                          # RustServer.ConfigFiles PluginManagement Source workspace
│   └── plugins/                    # RustServer.PluginFiles PluginManagement Source workspace
├── repos/                          # REPOS_DIR
│   ├── RustServer.Rcon/
│   ├── RustServer.Control/
│   ├── RustServer.Versions/
│   ├── RustServer.Plugins/
│   ├── RustServer.Snapshots/
│   └── RustServer.Web/
├── state/
│   ├── RustServer.Plugins/         # service-owned PluginManagement state
│   └── RustServer.Snapshots/       # service-owned persistent archive storage
├── packages/
│   ├── nuget/                      # NUGET_DIR
│   └── global-packages/            # workspace-local NuGet cache
└── NuGet.config                    # workspace-level package sources
```

The established host runtime location remains `/srv/rust/server`; it is not a second workspace runtime directory. Step 30 creates it when absent and never removes or recursively changes existing server data.

## Ordered pipeline

The orchestrator discovers numbered `*.sh` files under `scripts/bootstrap.d/` and sorts them by version-aware numeric filename order. The current pipeline is:

| Step | Behavior |
| --- | --- |
| `10-check-prerequisites.sh` | Requires Ubuntu 24.04, the initial Git/SSH/sudo prerequisites, and a platform Git worktree with an origin. |
| `20-install-development-tools.sh` | Installs and verifies the required development tooling. |
| `22-install-terminal-tools.sh` | Installs only missing SSH/TUI utilities and a permanent `tools`, `help`, `info` reference; skips entirely if all are already available. | |
| `25-install-nodejs.sh` | Installs or verifies Node.js, npm, and npx required by the frontend. |
| `30-create-platform-layout.sh` | Creates the repository and NuGet-feed directories and the established Rust runtime directory when missing. |
| `35-create-local-configuration.sh` | Safely creates or migrates canonical Rust settings and the canonical RCON secret, then validates shared topology and Compose. |
| `40`–`66` repository sync steps | Safely clone or fast-forward synchronize RustServer.Rcon, RustServer.Control, RustServer.Versions, RustServer.Plugins, RustServer.Web, and RustServer.Snapshots. |
| `63-sync-rustserver-plugins.sh` | Safely synchronizes the RustServer.Plugins service repository into `<WORKSPACE_ROOT>/repos/RustServer.Plugins`. |
| `65-sync-rustserver-web.sh` | Safely clones or fast-forward synchronizes `RustServer.Web`. |
| `70-build-rustserver-rcon-package.sh` | Configures the workspace NuGet sources, then restores, builds, tests, packs, and verifies `RustServer.Rcon`. |
| `75-build-rustserver-rcon-grpc.sh` | Builds `RustServer.Rcon.Grpc` in Release and verifies its runnable output. |
| `80-build-rustserver-control.sh` | Verifies Control's declared `RustServer.Rcon` package artifact, then restores, builds, and tests Control. |
| `82-install-runtime-assets.sh` | Idempotently installs the versioned Rust, SteamCMD, and Oxide Compose definitions and their colocated scripts under `/opt/rustserver/runtime/`; it does not copy configuration, secrets, or server data and does not invoke Docker. |
| `85-install-rustserver-control-service.sh` | Migrates Control's JSON state to its canonical location, ensures Control can atomically replace it, then installs or updates, reloads when changed, enables, starts or restarts, and verifies the `rustserver-control.service` systemd unit. |
| `86-install-rustserver-rcon-grpc-service.sh` | Installs or updates, reloads when changed, enables, starts or restarts, and verifies gRPC and OpenAPI for `rustserver-rcon-grpc.service`. |
| `90-build-rustserver-versions.sh` | Restores, builds, and tests the Versions solution as currently declared. |
| `91-build-rustserver-plugins.sh` | Restores, builds, tests, and verifies the committed Plugins OpenAPI contract. |
| `92-build-rustserver-web.sh` | Runs deterministic `npm ci` and the frontend-owned `npm run check`. |
| `93-install-rustserver-plugins-service.sh` | Provisions the independent Plugins credential; installs, enables, starts or restarts the Plugins service, which creates its Git source workspaces. Does not clone or validate `.git` in Bootstrap. |
| `94-install-rustserver-web-service.sh` | Installs, enables, starts, and validates `rustserver-web.service`. |
| `95-install-rustserver-versions-service.sh` | Installs or updates, reloads when changed, enables, starts or restarts, and verifies the `rustserver-versions.service` systemd unit. |
| `96-build-rustserver-snapshots.sh` | Restores, builds, tests, and verifies the Snapshots API contract. |
| `97-install-rustserver-snapshots-service.sh` | Installs or updates, enables, starts or restarts, and verifies the `rustserver-snapshots.service` systemd unit. |
| `99-validate-bootstrap.sh` | Rechecks host tooling and Docker availability after all step-specific validations have passed. |

Each step returns normal Unix statuses: exit code `0` means success (including a clear no-op for already-valid state); any non-zero code stops the orchestrator immediately. The root script prints the running step and either its success or its failed exit code, while preserving child output for diagnosis.

## Development tools

Step 20 uses Ubuntu packages for supporting tools and file/text utilities: CA certificates, `curl`, `file`, GnuPG, `gzip`, `jq`, `less`, `mc`, OpenSSL, ripgrep, `tar`, `unzip`, `wget`, and Ubuntu's `bat` package. It exposes Ubuntu's `batcat` binary as the expected `bat` command when necessary.

It installs .NET SDK 10 through Microsoft's Ubuntu repository and Docker Engine plus the Compose plugin through Docker's official Ubuntu repository. It refuses to silently replace conflicting Docker packages. Where systemd is present, it enables and starts Docker. Bootstrap also adds the invoking non-root user to the `docker` group if needed. During that first session, final validation falls back to `sudo docker` and warns that a new login session is required before using Docker without `sudo`.

When absent, lazygit, lazydocker, and TermIDE are installed from their latest official GitHub release assets after verifying the release API SHA-256 digest. The release repositories are declared beside the installation logic in step 20; the script selects an x86_64 or aarch64 Linux glibc tarball for TermIDE and installs `termide` in `/usr/local/bin`. Existing executable commands are retained rather than downloaded again. The step verifies .NET SDK 10 and all required commands before it succeeds.

Git itself is deliberately not installed or reconfigured by this step.

### Terminal tools and SSH quick reference (Step 22)

Step 22 installs terminal tools not already supplied by Step 20. It uses APT
for `micro`, `btop`, `tmux`, `ncdu`, `fzf`, `duf`, and `lnav`;
on supported architectures, it installs `systemctl-tui`, `lazyjournal`,
`yazi`, `dust`, and `hx` (Helix) from SHA-256-verified releases, and
`posting` through user-local pipx. Missing optional release tools emit
warnings without blocking core platform provisioning. Step 20 already owns
TermIDE, `mc`, `lazygit`, `lazydocker`, `bat`, `jq`, and `rg`.

**Fast path:** Bootstrap checks the installed commands, the exact
`/usr/local/bin/tools` helper, and the `~/.bashrc` marker *before*
APT, sudo, release downloads, or configuration. If everything is present,
Step 22 immediately returns `SKIP` without changing anything. If only
some programs are missing, only those programs are installed; existing
commands and aliases stay untouched.

The existing `deploy/cleanup.sh` removes the RustServer deployment but
**retains** host packages, `/usr/local/bin/tools`, and user shell settings,
so Step 22 normally skips after cleanup.

After the first installation, reconnect via SSH and run:

```bash
tools     # Short Russian descriptions of installed tools
help      # Same list in interactive Bash
info      # Same list in interactive Bash
help cd   # Normal Bash help still works
info bash # Normal GNU info still works
```

There is no `--all` option or missing-program catalog. No login shell
configuration is changed after the initial setup.

Step 25 installs the current Node.js 22 LTS release when the existing Node.js
version does not satisfy the frontend's `>=22.12` requirement. It verifies
that `node`, `npm`, and `npx` are available on `PATH` for later
non-interactive bootstrap steps.

## Rerunning and repository safety

The whole command is designed to be run again after correcting a failure. Existing directories are reused, installed tools are verified, an existing local configuration or secret is retained, and a matching RCON package artifact alone may be replaced during a fresh package build.

Each repository sync step declares its repository name, canonical workspace directory, and expected remote at its top. A missing directory is cloned. For an existing directory, bootstrap requires it to be the repository root of a Git working tree with the expected `origin`, a clean status (including no untracked files), and a branch tracking its canonical `origin/<same-branch>`. It then fetches and uses only a fast-forward-only merge.

The two PluginManagement source workspaces are deliberately configured at
`oxide/` and `oxide/plugins/` rather than under `repos/`.
**They are owned exclusively by RustServer.Plugins.** The service clones
missing workspaces from the two canonical HTTPS GitHub repositories onto
`develop` at first start and preserves existing worktrees and selected
`develop` or `release/<version>` branches on subsequent starts. Bootstrap
never mutates these checkouts, their remote refs, or the Plugins Baseline.
On failure, consult the Plugins Git diagnostics and systemd journal; do not
use Bootstrap to reset, repair, or synchronize these repositories.

New dependent clones use the configured GitHub SSH remotes. Existing HTTPS origins for the same GitHub repositories remain valid because the Git helper normalizes both SSH and HTTPS forms before comparing them.

Bootstrap never uses `git reset --hard`, `git clean`, a forced checkout, or a destructive replacement. It stops for a dirty worktree, a non-Git directory, a wrong origin, detached HEAD, a branch with no `origin` upstream, local-ahead/diverged history, or history that cannot fast-forward. Resolve that repository state manually, then rerun the full command.

## Frontend runtime

Step 65 synchronizes `RustServer.Web` at
`<WORKSPACE_ROOT>/repos/RustServer.Web`. Step 92 prepares its committed
dependency tree with `npm ci` and runs its frontend-owned `npm run check`
validation contract. Step 94 then installs, enables, starts, and verifies
`rustserver-web.service` automatically.

The service runs Vite's development server with `npm run dev` as the non-root
workspace owner. Its listener and proxy targets come from
`/etc/rustserver/topology.env`; Bootstrap waits for
`RUSTSERVER_WEB_URL/dashboard/` to respond directly with HTTP 200 before it
succeeds. A redirect does not satisfy readiness. This is a development-only
runtime. Production Installer installs and configures Nginx; static Web
releases are installed separately through Updater, without Node.js or Vite.

## Rust server configuration and secrets

Stages 1, 2, 6, and 7 of the runtime-configuration migration are complete for
Rust Dedicated Server settings, its RCON password, Control's desired/effective
state, and installed Compose assets. The canonical active files are
`/etc/rustserver/rust-server.env`, `/etc/rustserver/rcon-password`, and
`/var/lib/rustserver/control/server-configuration.json`; the checked-in
`docker/rust-server/.env.example` remains the non-secret settings template.

Step 35 never overwrites a valid canonical file. Once it exists,
`/etc/rustserver/rust-server.env` is authoritative: Bootstrap does not compare
or replace it based on the legacy `docker/rust-server/.env`. If the canonical
file is absent and the legacy file exists, Bootstrap validates and atomically
copies it, retaining the legacy file as a recoverable backup. If neither file
exists, it creates the canonical file from the template. Step 35 validates the
resulting `RUST_SERVER_PATH` and renders Rust Compose with
`--env-file /etc/rustserver/rust-server.env`, without starting a service.

Rust and SteamCMD Compose use the same canonical settings file. After Step 82,
routine Compose and Control operations use the installed definitions:

```bash
docker compose --env-file /etc/rustserver/rust-server.env -f /opt/rustserver/runtime/rust-server/compose.yml config
docker compose --env-file /etc/rustserver/rust-server.env -f /opt/rustserver/runtime/steamcmd/compose.yml config
```

### Installed Docker Compose runtime assets

Step 82 owns `/opt/rustserver/runtime/`. It installs only these versioned,
non-persistent files:

```text
/opt/rustserver/runtime/
├── rust-server/
│   ├── compose.yml
│   └── scripts/start-rust.sh
├── steamcmd/
│   ├── compose.yml
│   └── scripts/update-rust.sh
└── oxide/
    ├── compose.yml
    └── scripts/update-oxide.sh
```

The Compose files keep their relative `./scripts/...` mounts, so each installed
definition resolves its script in the same installed directory. Step 82
requires regular files and directories, installs files as `root:root` (`0644`
for Compose definitions and `0755` for scripts), and replaces an installed
asset only when its content, owner, or mode differs from the checked-in source.
It does not copy `.env` files, passwords, Control state, or Rust game data, and
it neither calls Docker nor restarts or recreates containers.

Step 85 configures Control with absolute installed Compose paths for lifecycle,
Rust update, Oxide update, wipe, and startup tracking. The service still loads
`/etc/rustserver/rust-server.env` and topology through `EnvironmentFile=`; the
Rust Compose secret remains `/etc/rustserver/rcon-password`. The direct
development launcher uses the same installed paths after Bootstrap has run.

Step 85 preserves an existing
`/var/lib/rustserver/control/server-configuration.json` as authoritative. If it
is absent and `docker/rust-server/server-configuration.json` exists, the step
atomically copies the JSON bytes to the canonical file and retains the legacy
file as a recoverable backup. It does not create a placeholder when neither
file exists; Control writes the initial file when it first saves configuration.
The dedicated state directory and canonical file are owned by the workspace
service user with modes `0700` and `0600`. This permits a same-directory
temporary file and atomic replacement without changing shared `/etc/rustserver`
access.

The checked-in `config/topology.env.example` is the sole template for platform
addresses. Step 35 installs it as `/etc/rustserver/topology.env` only when the
runtime file is absent, and preserves all existing values on later runs. The
file contains the Control, Versions, Plugins, Snapshots, Web, WebRCON, RCON
gRPC, and RCON Swagger/OpenAPI URLs.
It is intentionally separate from `/etc/rustserver/rust-server.env`; Compose
receives its required internal RCON port as a derived value from
`RUSTSERVER_RCON_URL` when Control starts Rust.

The six HTTP URLs are connection addresses and the source of truth for their
ports. Each owning HTTP application derives its port from its own URL, while
its matching `RUSTSERVER_*_BIND_HOST` key selects the IPv4 listening interface.
The default `0.0.0.0` values are temporary development configuration: URLs
continue to give same-host services stable loopback connection addresses while
trusted development-network clients can use the VM IP. Bind hosts are required
canonical IPv4 addresses, so a missing or invalid value fails startup clearly.
WebRCON is different: `RUSTSERVER_RCON_URL` continues to describe the
loopback-only Rust WebRCON endpoint and does not configure an HTTP listener.
`RUSTSERVER_RCON_GRPC_URL` is the loopback-only HTTP/2 service-discovery address
for gRPC clients. `RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL` is the separate,
loopback-only HTTP/1.1 endpoint for Swagger and OpenAPI. The topology parser
rejects a non-loopback address, a non-HTTP URL, or an invalid port for either
RCON gRPC endpoint before a unit is installed.

On an upgrade, step 35 preserves every existing topology value and appends only
missing RCON gRPC URLs and bind-host keys with the documented development
defaults. Legacy `RUSTSERVER_PLUGIN_CATALOG_*` entries are preserved but
ignored so an old topology file remains valid while Catalog moves to its
own explicit provisioning configuration. It never replaces the runtime file, rejects duplicate active keys, and is
idempotent on later Bootstrap runs.

The one active RCON secret is `/etc/rustserver/rcon-password`. It contains one
raw password, without a `KEY=value` prefix. If it exists, it is authoritative:
Bootstrap does not compare it with or replace it from the legacy checkout
secret. If it is absent and `docker/rust-server/.rcon-password` exists,
Bootstrap atomically copies that value. If neither file exists, Bootstrap
generates a cryptographically random password with OpenSSL. The canonical file
is owned by the workspace owner and has mode `0600`, so the owner and
its Rcon and Snapshots services (and root) can read it; no other account can.
The password is never printed by Bootstrap or this documentation.

The legacy `.env` and `.rcon-password` remain ignored by
`docker/rust-server/.gitignore` as recoverable migration backups; neither is
an active configuration source. `/etc/rustserver/rust-server.env`,
`/etc/rustserver/rcon-password`, and topology live outside the repository.
Templates may be versioned; actual configuration and secrets must not be
committed.

`scripts/dev/run-control.sh` and `scripts/dev/with-topology.sh` parse and export
the topology file without sourcing it as shell code. Use `with-topology.sh`
before `dotnet run`, a published DLL, or Vite when outside systemd. The RCON
password comes from `/etc/rustserver/rcon-password` unless
`RustServerRcon__Password` is explicitly provided.

## TODO: Production Network Isolation

### Current development environment

The default `0.0.0.0` bind hosts make Control, Versions, Plugins, Snapshots,
and Web directly reachable from the trusted development network. This is
intentional and temporary for development and testing.

### Future public deployment

Only RustServer.Web should be publicly reachable. Control, Versions, Plugins,
and Snapshots must remain internal to the platform. WebRCON and RCON gRPC,
including its Swagger/OpenAPI endpoint, must remain local. Public access must
eventually be placed behind an appropriate reverse proxy with authentication,
HTTPS, and firewall restrictions.

This transition must require only editing `/etc/rustserver/topology.env` and
restarting the affected services:

```ini
RUSTSERVER_CONTROL_BIND_HOST=127.0.0.1
RUSTSERVER_VERSIONS_BIND_HOST=127.0.0.1
RUSTSERVER_PLUGINS_BIND_HOST=127.0.0.1
RUSTSERVER_SNAPSHOTS_BIND_HOST=127.0.0.1
RUSTSERVER_WEB_BIND_HOST=0.0.0.0
```

This does not implement the required reverse proxy, authentication, HTTPS, or
firewall controls. Bind-host settings only make the listener placement
changeable without rebuilding the applications.

## NuGet flow

Step 70 creates `<WORKSPACE_ROOT>/NuGet.config` from `scripts/bootstrap/NuGet.config.template` only when it is absent. An existing workspace configuration is preserved, but it must expose both enabled `nuget.org` and the physical local feed at `<WORKSPACE_ROOT>/packages/nuget`. The versioned template uses portable relative paths for both the feed and `globalPackagesFolder` at `<WORKSPACE_ROOT>/packages/global-packages`, not a user-specific home directory.

The RCON step obtains its package ID and version from the RCON project metadata, then runs restore, Release build with warnings treated as errors, Release test, and pack. It verifies the freshly packed archive before moving it to exactly:

```text
<WORKSPACE_ROOT>/packages/nuget/<PackageId>.<PackageVersion>.nupkg
```

Only that matching generated artifact is replaced; the rest of the local feed is retained. After a fresh package is in the feed, step 70 removes only its exact package ID/version directory from the workspace-local global-packages cache. Step 80 forces a restore using that workspace cache, verifies its extracted package metadata, and then builds/tests Control. The user's normal global NuGet cache is never cleared, and no manually remembered package-output location or cross-repository project reference is needed.

Step 75 explicitly builds the RCON gRPC executable from the solution restore
owned by step 70 and verifies its DLL, dependency manifest, and runtime
configuration. This preserves the existing library package flow while making
the executable artifact an explicit bootstrap concern.

Step 35 validates local configuration, topology, and the RCON secret; repository sync steps validate their own Git state; and build steps validate their own NuGet configuration and artifacts. Step 85 owns the Control-state migration and systemd unit, and verifies Docker access as the workspace owner; step 86 owns the RCON gRPC unit; step 93 owns the Plugins unit and verifies that its host responds; step 94 owns the Web unit and its HTTP readiness; step 95 owns the Versions systemd unit; step 97 owns the Snapshots unit, its persistent archive storage, and its HTTP readiness. Step 99 only rechecks host tooling and a reachable Docker Engine; it does not contain service-specific behavior.

## RustServer.Rcon gRPC runtime

Step 86 installs `rustserver-rcon-grpc.service` in `/etc/systemd/system/`.
The unit runs the Release output built by step 75 as the non-root workspace
owner and imports `/etc/rustserver/topology.env` with `EnvironmentFile=`. At
process startup its Bash launcher reads `/etc/rustserver/rcon-password` and
exports `RustServerRcon__Password` only to the Rcon process. The generated unit
contains no password value, and no second secret is created.

The unit has no Docker, Control, or Rust Dedicated Server ordering dependency.
It starts independently while Rust is stopped; RCON operations naturally need
the Rust WebRCON endpoint to be available when they are invoked. It restarts on
unexpected failure. Bootstrap verifies all three conditions after start:
`systemctl is-active`, a reachable HTTP/2 gRPC listener at
`RUSTSERVER_RCON_GRPC_URL`, and `openapi.json` at
`RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL`. Both listeners are constrained to
localhost by topology validation and by the executable's own configuration
validation.

Manage the service with normal systemd commands:

```bash
sudo systemctl start rustserver-rcon-grpc
sudo systemctl stop rustserver-rcon-grpc
sudo systemctl restart rustserver-rcon-grpc
sudo systemctl status rustserver-rcon-grpc
sudo systemctl is-active rustserver-rcon-grpc
journalctl -u rustserver-rcon-grpc --no-pager -n 50
```

The service endpoints are:

```text
gRPC:          $RUSTSERVER_RCON_GRPC_URL
Swagger UI:    $RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL/swagger
OpenAPI:       $RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL/openapi.json
```

## RustServer.Versions runtime

Step 95 installs `rustserver-versions.service` in `/etc/systemd/system/`. The unit runs the Release output built by step 90, not `dotnet run`, as the non-root owner of the canonical workspace. It sets `ASPNETCORE_ENVIRONMENT=Development`, so the Versions Swagger UI is available at `/swagger`. It consumes `/etc/rustserver/topology.env` with `EnvironmentFile=`, listens on `RUSTSERVER_VERSIONS_URL`, restarts after unexpected failures, and writes its process output to the system journal.

The service maps the canonical `RUST_SERVER_PATH` value from
`/etc/rustserver/rust-server.env` to the API's existing
`RustVersions__AppManifestPath` setting at:

```text
<RUST_SERVER_PATH>/steamapps/appmanifest_258550.acf
```

`RustVersions:Branch` keeps its checked-in default of `public`; the installer does not create a second Versions configuration file or configuration name. Bootstrap requires the built `RustServer.Versions.Api.dll` and fails with its expected path if it is absent. Rust Dedicated Server content is still installed separately, so the API can be active before its app manifest exists; its version endpoint will report that missing-manifest configuration at request time until Rust is installed.

Manage the service with normal systemd commands:

```bash
sudo systemctl start rustserver-versions
sudo systemctl stop rustserver-versions
sudo systemctl restart rustserver-versions
sudo systemctl status rustserver-versions
sudo systemctl is-active rustserver-versions
journalctl -u rustserver-versions --no-pager -n 50
```

The API endpoint is `GET /api/versions/rust`:

```bash
curl "$RUSTSERVER_VERSIONS_URL/api/versions/rust"
```

## RustServer.Plugins runtime

Step 91 restores, builds, and tests `RustServer.Plugins.sln` with the workspace `NuGet.config` and `packages/global-packages` cache. The solution includes `RustServer.Plugins.ApiContractGenerator`, so its restore assets and Release output are established before the tool runs in non-writing `--no-build --no-restore --verify` mode. Verification compares the running generated document with committed `openapi.json` and fails without rewriting it when the transport artifact is stale.

Step 93 installs `rustserver-plugins.service` in `/etc/systemd/system/`. The unit runs the Release `RustServer.Plugins.dll` from `src/RustServer.Plugins` as the non-root workspace owner and consumes `/etc/rustserver/topology.env` with `EnvironmentFile=`. It listens on `RUSTSERVER_PLUGINS_URL` and uses `RUSTSERVER_CONTROL_URL` for its Control dependency. It creates and preserves the service-owned persistent state root at `<WORKSPACE_ROOT>/state/RustServer.Plugins`, verifies that the service user has read/write/traverse access to it, and supplies it through `PluginManagement__StateRootPath`. The current Environment, Baseline, and SQLite operation history database (`operations.db`) are stored there; the database path is explicitly supplied through `PluginManagement__OperationHistory__DatabasePath`. Bootstrap never removes or resets this state during a rerun. It configures the target paths for Plugins-owned Configs and Plugins Git workspaces at `<WORKSPACE_ROOT>/oxide` and `<WORKSPACE_ROOT>/oxide/plugins`, plus `RUST_SERVER_PATH` as `PluginManagement__Runtime__ServerRootPath`.

The installer checks that the non-root service user can create directories
beneath `<WORKSPACE_ROOT>`, and can read/write/traverse the Rust runtime root.
It does **not** require either source repository or a `.git` directory to
exist. It provisions a separate Plugins GitHub token under
`/etc/rustserver/plugins-github-token` (root:root, mode 0600) and supplies
it via systemd `LoadCredential`. RustServer.Plugins, not Bootstrap, creates
and manages the two source workspaces.

After enable/start or restart, step 93 requires `systemctl is-active rustserver-plugins.service`, then verifies that the host responds at `/`. A `404` response is expected because the foundation exposes no business or dedicated readiness endpoint. OpenAPI verification is a separate build-time check; Swagger/OpenAPI is not a service-readiness dependency.

Manage the service with normal systemd commands:

```bash
sudo systemctl start rustserver-plugins
sudo systemctl stop rustserver-plugins
sudo systemctl restart rustserver-plugins
sudo systemctl status rustserver-plugins
sudo systemctl is-active rustserver-plugins
journalctl -u rustserver-plugins --no-pager -n 50
```

## Adding a future step

Add one self-contained executable script named `NNN-description.sh` under `scripts/bootstrap.d/`, for example `100-prepare-docker-services.sh`. Declare the paths, repositories, inputs, and validations used by that step near its top; source only `scripts/lib/common.sh` for generic logging/error helpers and `scripts/lib/git.sh` when using safe Git synchronization. Use the same `0`/non-zero contract, make existing-valid state a logged `0`-exit no-op, and avoid destructive recovery. The orchestrator will discover and run the new numeric step after `99` without changing `scripts/bootstrap.sh`.
