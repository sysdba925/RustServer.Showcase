# RustServer Platform — Runtime Configuration Layout

**Status:** Approved target layout. Stages 1 and 2 migrate Rust settings and
the RCON password; Stage 4 removes the persistent Production Bootstrap
configuration; Stage 5 migrates the three Updater files to the flat layout;
Stage 6 migrates Control's persistent desired/effective JSON state; Stage 7
installs the Docker Compose runtime asset set; Stage 8 standardizes the
Installer-provisioned Auth environment file.
**Scope:** Development and Production installations.  
**Owner:** `rust-server-platform` (Bootstrap provisions the configuration; installed services consume it).

**Audit note (13 October 2026):** The separately named decision record
`rustserver-runtime-configuration-migration-decision.md` was not present in
this checkout, its local Git history/branches, or the available sibling
repositories. This document therefore remains the available target contract;
the audit facts and conflicts that cannot be resolved without that decision
record are listed in
[runtime-configuration-migration.md](runtime-configuration-migration.md).

## 1. Directory layout

```text
/etc/rustserver/                         # Persistent platform configuration; no subdirectories
├── topology.env                        # URLs, ports and bind addresses
├── rust-server.env                     # Rust Dedicated settings
├── rcon-password                       # Shared WebRCON password (raw text, one line)
├── updater.env                         # Updater HTTP listener settings
├── updater-applications.ini            # Application deployment registry
├── updater-release-channel            # Development or Production
├── updater-github-token                # GitHub Releases credential for Updater
└── auth.env                            # Auth Google and first-admin configuration

/opt/rustserver/                         # Installed software, not editable configuration
├── updater/
├── control/
├── rcon/
├── versions/
├── plugins/
├── snapshots/
├── auth/
├── web/
└── runtime/                            # Root-managed Docker Compose runtime assets
    ├── rust-server/
    │   ├── compose.yml
    │   └── scripts/start-rust.sh
    ├── steamcmd/
    │   ├── compose.yml
    │   └── scripts/update-rust.sh
    └── oxide/
        ├── compose.yml
        └── scripts/update-oxide.sh

/srv/rust/server/                        # RustDedicated installation and its game data
/var/lib/rustserver/                    # Persistent application state
└── control/
    └── server-configuration.json       # Control desired/effective server state
```

The `/etc/rustserver/` layout is deliberately **flat**: no `/etc/rustserver/updater/` directory.
The shared directory is `root:root`, mode `0755`, so independent Development
and Production service accounts can traverse it. Access to configuration and
secret contents is controlled by each individual file's owner, group, and mode.

The installed directories retain the historical Compose project basenames
(`rust-server`, `steamcmd`, and `oxide`). Docker Compose therefore continues to
resolve each relative `./scripts/...` mount from the supplied Compose file's
directory. Control's service working directory remains its release directory;
all of its Compose calls use absolute `-f /opt/rustserver/runtime/.../compose.yml`
paths, so that process working directory is not a runtime-asset dependency.

## 2. Configuration file contract

| File | Contents / keys | Readers |
| --- | --- | --- |
| `topology.env` | `RUSTSERVER_CONTROL_URL`, `RUSTSERVER_VERSIONS_URL`, `RUSTSERVER_PLUGINS_URL`, `RUSTSERVER_SNAPSHOTS_URL`, `RUSTSERVER_WEB_URL`, `RUSTSERVER_RCON_URL`, `RUSTSERVER_RCON_GRPC_URL`, `RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL`, and service bind-host settings where applicable. | Services and launch/systemd configuration |
| `rust-server.env` | `RUST_SERVER_PATH`, `RUST_SERVER_PORT`, `RUST_SERVER_QUERY_PORT`, `RUST_SERVER_LEVEL`, `RUST_SERVER_SEED`, `RUST_SERVER_WORLD_SIZE`, `RUST_SERVER_MAX_PLAYERS`, `RUST_SERVER_HOSTNAME`, `RUST_SERVER_DESCRIPTION`, `RUST_SERVER_TICK_RATE`, `RUST_SERVER_SAVE_INTERVAL`, `RUST_SERVER_IDENTITY`. | Rust Docker Compose; Control's server configuration integration |
| `rcon-password` | A single raw WebRCON password; no `KEY=value` prefix. | Rust Docker Compose (secret mount), `RustServer.Rcon` gRPC host, Snapshots host, and the Control development launcher |
| `updater.env` | `RUSTSERVER_UPDATER_BIND_ADDRESS`, `RUSTSERVER_UPDATER_PORT`. | Updater systemd service |
| `updater-applications.ini` | `[Defaults]` and `[Application:<name>]` registrations: repository, deployment directory, service/entrypoint or static entry file, readiness port/path. | Updater and privileged Deployment Executor |
| `updater-release-channel` | A single value: `Development` or `Production`. | Updater |
| `updater-github-token` | A token for reading private GitHub Releases. | Updater through its systemd credential mechanism |
| `auth.env` | Required on every Auth start: `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`. On the first start of an absent or empty Auth database: `ADMIN_USERNAME`, `ADMIN_PASSWORD`; optional `Bootstrap__AdminUserName` must match `ADMIN_USERNAME` when set. | systemd `EnvironmentFile`, RustServer.Auth, and the Deployment Executor preflight |

The update **release channel** and `ASPNETCORE_ENVIRONMENT` are different concepts. Changing the release channel is not the same as changing ASP.NET Core's runtime environment.

Production creates loopback bind hosts in `topology.env` and a loopback Updater listener. Auth�s newly provisioned systemd unit specifies its loopback URL; Rcon consumes its loopback endpoints from topology. Nginx is the public HTTP entry point. Application releases and application-owned provisioning must retain loopback listeners, and operators must verify actual sockets after activation. Existing configuration and units require an explicit operator migration. Rust game ports and SSH are independent of this HTTP boundary.

## 3. Configuration ownership and lifecycle

| Operation | Rule |
| --- | --- |
| First install | Production Installer interactively collects missing Auth credentials; Bootstrap validates its private handoff and atomically creates `auth.env`. Other missing files come from reviewed templates and Bootstrap generates the RCON password when absent. |
| Reinstall / Bootstrap rerun | Existing configuration and user values are preserved; never silently overwrite them. |
| Runtime asset refresh | Development Bootstrap updates only the six root-managed Compose/script files below `/opt/rustserver/runtime/`; it does not copy configuration, secrets, state, or game data and does not invoke Docker. |
| Application install / update / rollback | Versions of binaries change; configuration in `/etc/rustserver/` persists unchanged. |
| Removing the Bootstrap source tree | Installed services, Docker Compose and configuration must continue working; no runtime path may point into the repository. |
| Changing Rust options | Edit `rust-server.env`; apply through an explicit, appropriate restart or configuration procedure. |
| Changing WebRCON password | Edit **one** `rcon-password` file; restart/reload the Rust, Rcon, and Snapshots consumers as appropriate. |
| Updating Control configuration | Control atomically replaces `/var/lib/rustserver/control/server-configuration.json` after an API update or successful start records the effective state. |
| Releasing or reinstalling Updater | Preserve its registry, channel, token, listener settings and durable state. |
| Upgrading Updater | Stage its paired Deployment Executor without replacing the active program; while Updater is stopped, atomically activate both and retain enough transaction state to restore the previous pair. |
| Installing or upgrading Auth | Preserve `auth.env` and `/var/lib/rustserver/auth/auth.db`. On first install Bootstrap create-only persists Installer-supplied credentials; an Auth release is activated only after the Deployment Executor validates the canonical configuration. |

**Single source of truth:** Rust Dedicated, Rcon, Snapshots, and the Control
development launcher share the same physical `rcon-password` file. The
launcher supplies it only when explicitly starting Control outside systemd.

**Control state ownership:** The Development Bootstrap service user owns
`/var/lib/rustserver/control/` with mode `0700` and its
`server-configuration.json` file with mode `0600`. The dedicated state
directory allows a same-directory temporary file and atomic replacement without
changing shared `/etc/rustserver` access. No source checkout path is an active
reader after the migration.

**Auth environment ownership:** `/etc/rustserver/auth.env` is the sole
persistent Auth environment file. The Production Installer owns first-run
interactive collection and gives Bootstrap a mode-`0600` temporary handoff;
Bootstrap atomically creates the canonical file as `root:rustserver-auth`,
mode `0640`. systemd reads the `EnvironmentFile` during service launch and
passes the resulting environment to `rustserver-auth`. An existing valid file
is authoritative and is never replaced automatically; an invalid existing file
fails with a diagnostic. Bootstrap, the Deployment Executor, and application
release operations never print or duplicate its values.

## 4. Migration from the current repository-dependent layout

This is the active contract. **Stages 1 and 2 migrated**
`docker/rust-server/.env` to `/etc/rustserver/rust-server.env` and
`docker/rust-server/.rcon-password` to `/etc/rustserver/rcon-password`.
**Stage 5 migrated** the three Updater paths below. The legacy Updater files
remain recoverable backups until a separately approved cleanup.
**Stage 6 migrated** Control's JSON state, preserving the checkout-local file
as a recoverable backup. **Stage 7 installs** the Rust, SteamCMD, and Oxide
Compose definitions plus their colocated launch/update scripts below
`/opt/rustserver/runtime/`. **Stage 8 creates** missing
`/etc/rustserver/auth.env` from the Installer's temporary credential handoff
without creating a configuration subdirectory; an existing valid file remains
authoritative.

| Existing location / behavior | Destination / action |
| --- | --- |
| `rust-server-platform/docker/rust-server/.env` | Preserve its actual values, migrate to `/etc/rustserver/rust-server.env`. |
| `rust-server-platform/docker/rust-server/.rcon-password` | Retain as a recoverable legacy backup. Step 35 copies it only when `/etc/rustserver/rcon-password` is absent. |
| `rust-server-platform/docker/rust-server/server-configuration.json` | Stage 6 copies its JSON bytes atomically to `/var/lib/rustserver/control/server-configuration.json` only when that canonical file is absent. The canonical file is authoritative and the legacy file remains a recoverable backup. |
| `/etc/rustserver/topology.env` | Keep in place; update readers only where required. |
| `/etc/rustserver/updater.env` | Keep in place. |
| `/etc/rustserver/bootstrap.env` | Legacy installer-only configuration. Production Bootstrap no longer reads, creates, replaces, or deletes it; retain any existing file until a separate cleanup migration. |
| `/etc/rustserver/updater/applications.ini` | Stage 5 migrates it to `/etc/rustserver/updater-applications.ini`, preserving registration contents, owner, and mode. The legacy file remains a backup. |
| `/etc/rustserver/updater/release-channel` | Stage 5 migrates it to `/etc/rustserver/updater-release-channel`, preserving its value, owner, and mode. The legacy file remains a backup. |
| `/etc/rustserver/updater/github-token` | Stage 5 migrates it to `/etc/rustserver/updater-github-token`, preserving the credential, owner, and mode. The legacy file remains a backup. |
| `/etc/rustserver/auth.env` | Stage 8 atomically creates this canonical file only when absent from the Installer's private handoff. It validates and preserves an existing file byte-for-byte; it never overwrites or migrates credentials. |
| Docker Compose and Rust startup script paths under the development checkout | Step 82 installs the three Compose definitions and their scripts to `/opt/rustserver/runtime/`. Step 85 and the direct Control launcher use absolute paths there; the checkout copies remain versioned source definitions and Bootstrap-only migration inputs. |
| Development Rcon gRPC systemd unit that embeds the password as an environment value | Read the shared password from the persistent file, using a consistent supported service-start mechanism; do not generate a second secret. |

### Implementation sequence (mandatory)

1. **Inventory references first.** Find every read/write of old paths in production Bootstrap, development Bootstrap, Docker Compose, Control, Rcon gRPC, Updater, Deployment Executor, systemd units, tests and documentation. Do not assume only the paths listed above exist.
2. **Run non-destructive migration before changing consumers.** Create missing persistent files and copy existing values before consumers switch. For the three Stage 5 Updater files, an existing valid canonical file is authoritative, while a legacy-only file is atomically copied with its owner and mode preserved; the legacy source remains a backup.
3. **Install runtime assets outside Git.** Docker Compose files and the Rust launcher must operate entirely out of `/opt/rustserver/runtime/`, using `/etc/rustserver/` configuration and `/srv/rust/server/` data. Validate Docker bind mounts and paths relative to the installed Compose file.
4. **Switch consumers.** Update all startup scripts, service units, Updater/Executor registries, Rcon secret loading and Control configuration. Ensure all relevant processes can read their settings. Keep existing installed releases and deployment state intact.
5. **Validate development and installed-release flows.** Check Compose configuration, service startup, Rcon connection, Control lifecycle, Updater discovery, Prepare/Update/Rollback, and operation reconciliation on a disposable VM. Verify services still run **after the Bootstrap checkout is renamed or removed**.
6. **Clean up only after verification.** Remove obsolete copies, compatibility paths, templates or documentation from the active runtime as appropriate; preserve user data, backups, and historical changelog. Never delete the original configuration as part of a failed migration.

## 5. Documentation and repository hygiene

**Canonical document:** `rust-server-platform/docs/architecture/runtime-configuration-layout.md`.

- Update `deploy/bootstrap/README.md`, `scripts/bootstrap/README.md`, `RUST_DEV_RUNBOOK.md` (wherever that runbook is maintained), and any other instructions **in the same implementation change** so none direct operators to obsolete paths.
- Keep `.env.example` or equivalent **templates in Git** as templates only.
  `deploy/bootstrap/bootstrap.env.example` is an installation-default template
  parsed from the temporary Production Bootstrap archive; it is not runtime
  configuration and must not be copied to `/etc/rustserver/`.
- Add a migration entry to the platform changelog following existing conventions.
- Review whether `docs/architecture/technology-and-service-direction.md` and other architecture docs describe paths that must now change; link to this canonical document rather than duplicating the tables.
- Update repository `AGENTS.md` guidance, if present, so future agent changes follow this single-source-of-truth configuration contract.
- Do not delete existing docs or old runtime configuration files merely because this document has been added. Cleanup belongs to the tested migration, not the documentation commit.

## 6. Acceptance criteria

- Every installed consumer reads its runtime configuration from the agreed persistent locations.
- Exactly one active RCON password file exists and Rust, Rcon, and Snapshots use it.
- Updater and Executor agree on a **single** persistent application registry path.
- Existing user values, tokens, versioned releases and state survive migration.
- Re-running Bootstrap does not overwrite an administrator's configuration.
- Renaming or deleting the Bootstrap/source checkout does not break already installed services, including Rust Docker Compose lifecycle.
- Development and Production installation flows have matching persistent file layout; differing release-channel policy remains intact.
