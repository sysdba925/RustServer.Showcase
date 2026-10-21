# Runtime configuration migration inventory

**Status:** Stages 1, 2, and 4 are implemented for Rust settings, the RCON
password, and removal of persistent Production Bootstrap configuration. Stage
5 implements the Updater registry, release-channel, and token relocation.
Stage 6 moves Control's persistent desired/effective JSON state. Stage 7
installs the Rust, SteamCMD, and Oxide Compose runtime assets under
`/opt/rustserver/runtime/`. Stage 8 standardizes the Installer-provisioned
Auth environment at `/etc/rustserver/auth.env`. Stage 9 reconciles the shared
configuration-directory traversal permissions for independent Development and
Production service accounts. The target layout is described in
[runtime-configuration-layout.md](runtime-configuration-layout.md). This
inventory retains the original dependency audit and records the implemented
compatibility boundary for installed Updater releases.

This inventory is based on the checked-out platform sources and the sibling
application repositories reviewed on 13 October 2026. It describes source
dependencies, not an assertion about every historical customization on an
installed host. Before implementation, repeat the inventory against the exact
releases and host units being migrated.

## Target contract

The sole persistent platform configuration directory will be the flat
`/etc/rustserver/` directory specified by the
[canonical layout](runtime-configuration-layout.md). Its approved files are
`topology.env`, `rust-server.env`, `rcon-password`, `updater.env`,
`updater-applications.ini`, `updater-release-channel`, and
`updater-github-token`, and `auth.env`. Control's durable desired/effective state belongs at
`/var/lib/rustserver/control/server-configuration.json`. No persistent Bootstrap configuration file or
configuration subdirectory below `/etc/rustserver/` is allowed.

Installed application binaries belong below `/opt/rustserver/`, installed
Compose and Rust launch assets below `/opt/rustserver/runtime/`, and Rust
Dedicated data below `/srv/rust/server/`.

## Stage 1 audit — contract and dependencies

### Evidence and boundary

The requested agreed decision record,
`rustserver-runtime-configuration-migration-decision.md`, was not found in
this repository, its available local Git branches/history, or the available
sibling repositories. The findings below compare the checked-out sources with
the target contract above and the layout document only. They deliberately do
not settle an omitted-file, ownership, or service-boundary question.

The review covered both Bootstrap implementations, their generated unit and
Compose inputs, deployment assets and tests, and the available sibling
repositories: `RustServer.Auth`, `RustServer.Control`, `RustServer.Rcon`,
`RustServer.Snapshots`, `RustServer.Updater`, `RustServer.Versions`,
`RustServer.Plugins`, and `RustServer.Web`. A path search also covered
templates, runbooks, tests, OpenAPI/README material, and contract generators.
It is source evidence, not a claim about uninspected historical VM overrides.

### Bootstrap-to-contract comparison (updated after Stage 5)

| Contract area | Development Bootstrap (`scripts/bootstrap.d`) | Production Bootstrap (`deploy/bootstrap`) | Result |
| --- | --- | --- | --- |
| Flat `/etc/rustserver/` directory | Step 35 creates `topology.env`, `rust-server.env`, and `rcon-password` directly below it. The helper creates the directory as `0755`. | Steps 30 and 55 ensure `/etc/rustserver` is `root:root`, `0755`, and Step 55 creates or migrates the Updater files directly below it. | Both Bootstraps preserve the flat layout and independent service accounts can traverse the shared directory; individual file modes protect Updater and Auth secrets. |
| Rust settings | Step 35 retains the legacy `.env` only as a fallback and creates `/etc/rustserver/rust-server.env` without replacement. | Does not create or consume Rust settings. | Development implementation matches the existing migration policy; there is no production Bootstrap equivalent. |
| RCON secret | Step 35 preserves an existing canonical secret, copies the legacy raw value only when canonical is absent, otherwise generates it; it then enforces workspace-owner `0600`. | Does not provision or consume this secret. | Direct development consumers already use the canonical file; production ownership/service delivery remains unspecified. |
| Updater listener | Not owned by this Bootstrap. | Step 40 creates `/etc/rustserver/updater.env` with `0640`. | Matches the flat target filename, but its final reader access model must remain valid after directory reconciliation. |
| Updater registry, channel, token | Not owned by this Bootstrap. | Step 55 uses `/etc/rustserver/updater-applications.ini`, `/etc/rustserver/updater-release-channel`, and `/etc/rustserver/updater-github-token`; Step 60 maps the canonical token through `LoadCredential`. | Stage 5 moves all three together and keeps the legacy files as inactive recoverable backups. |
| Bootstrap defaults and Auth environment | Not owned by this Bootstrap. | Bootstrap parses `deploy/bootstrap/bootstrap.env.example` from its downloaded archive on each run and does not persist it; the Production Installer privately hands off first-run Auth credentials and Bootstrap atomically creates the canonical `/etc/rustserver/auth.env`. | Stage 8 makes `auth.env` an approved flat configuration file. Existing valid values are authoritative and never replaced. |
| Runtime assets | Step 82 installs Compose definitions and their colocated scripts under `/opt/rustserver/runtime/`; the Control unit and direct launcher use those absolute paths. | Updater, Auth, and Web releases execute from `/opt/rustserver`; deployment assets operate there. | The installed Rust lifecycle, Rust update, Oxide update, wipe, and startup tracking no longer depend on the platform checkout. |

### Consumer, owner, and access matrix

Observed modes are current source behavior. “Required access” is the minimum
access implied by the current consumer, not a newly approved ownership design.

| Artifact / current path | Writer and owner in source | Direct readers / consumers | Observed or required access | Target state and dependency |
| --- | --- | --- | --- | --- |
| `/etc/rustserver/topology.env` | Development step 35 creates it from a template (`0644`); it may append missing development keys. | `topology.sh`, `run-control.sh`, and development Control, Rcon, Versions, Plugins, Snapshots, and Web units. | Readers need read access; the shared directory/file are `0755`/`0644`. | Remains flat and readable by the independent Development service user. |
| `/etc/rustserver/rust-server.env` | Development step 35, via `rust_server_migrate_configuration`; canonical file is create-only and currently `0644`. | Rust and SteamCMD Compose rendering, Control, Plugins, Versions, Snapshots, and `run-control.sh`. | Each consuming service/Compose invocation needs read access. | Remains flat. Control additionally has a separate writable JSON state file; it is not represented by this file. |
| `/etc/rustserver/rcon-password` | Development step 35; workspace owner/group, `0600`; copies legacy bytes only when canonical is absent or securely generates a raw value. | Rust Compose secret mount, `run-control.sh`, Rcon gRPC launcher, Snapshots launcher. | The launcher identity (or root before privilege drop) needs read access; no unit should contain the raw value. | Remains flat. Development delivery is implemented; a production identity/credential-delivery policy is still absent. |
| `/var/lib/rustserver/control/server-configuration.json` | Control atomically writes desired/effective state. Step 85 copies the legacy JSON only while this canonical file is absent, then assigns the dedicated state directory and file to the Control service user. | Control API and any operator using its server-configuration endpoint. | Control owns its `0700` state directory and `0600` JSON file, allowing same-directory atomic replacement. | Active Stage 6 canonical Control state. An existing canonical file is authoritative. |
| `deploy/bootstrap/bootstrap.env.example` | Version-controlled installation defaults are parsed as data from the downloaded Production Bootstrap archive. | Production steps 40, 50, 55, and 80. | Bootstrap process read access only; no `/etc` file is created. | Installation-only input, not runtime configuration. Same-named supported environment values override it for the current invocation. |
| `/etc/rustserver/bootstrap.env` | Legacy file from prior Bootstrap versions; Stage 4 does not create, read, replace, or delete it. | No active Production Bootstrap consumer. | None. | Retain in place until a separate cleanup migration. |
| `/etc/rustserver/updater.env` | Production step 40 creates it atomically, root-owned `0640`. | Updater service `EnvironmentFile`; production steps 40, 55, 60, 70, and 80. | systemd/PID 1 reads it before service start; Bootstrap/root reads it. | Already flat; retain semantics while other updater files are migrated. |
| `/etc/rustserver/updater-applications.ini` | Step 55 creates it from the reviewed template or atomically copies the legacy INI; `root:rustserver-updater`, `0640`. | Updater and root-owned Deployment Executor. | Updater needs group read only; executor/root needs read; neither consumer writes registrations. | Active Stage 5 registry. An existing canonical file is authoritative. |
| `/etc/rustserver/updater-release-channel` | Step 55 creates/preserves it or atomically copies the legacy value; `root:rustserver-updater`, `0640`. | `RustServer.Updater`. | Updater needs group read; Bootstrap/root writes. | Active Stage 5 release-channel source. |
| `/etc/rustserver/updater-github-token` | Step 55 creates/preserves it or atomically copies the legacy credential; `root:root`, `0600`. | systemd `LoadCredential`, then Updater through `RUSTSERVER_UPDATER_GITHUB_TOKEN_FILE`. | Source token stays root-readable only; Updater receives only systemd's private credential copy. | Active Stage 5 token source and masked systemd path. |
| `/etc/rustserver/updater/{applications.ini,release-channel,github-token}` | Legacy files from pre-Stage-5 hosts. | No Stage 5 source consumer reads them after a compatible Updater release is activated. | Kept unchanged as recoverable backups. | Cleanup is separately approved work. |
| `/etc/rustserver/auth.env` | Production Installer supplies first-run values through a mode-`0600` temporary handoff; Bootstrap creates the file only when absent. | Auth unit and deployment executor preflight; Auth repository deploy unit and docs. | `root:rustserver-auth`, `0640`; systemd reads the `EnvironmentFile` and provides its values to the service without changing the shared directory. | Active Stage 8 Auth configuration. An existing valid canonical file is authoritative and is never replaced. |
| `/opt/rustserver/*` releases and `/var/lib/rustserver/*` state | Bootstrap/deployment executor install immutable releases; applications own their durable state. | Updater, Auth, Web and deployment recovery; Snapshots archives separately. | Release trees are root-managed/readable; state needs only its owning service write access. | These are assets/state, not substitutes for `/etc` configuration. Development runtime assets still require installed copies under `/opt/rustserver/runtime/`. |

### Snapshots and Rcon gRPC findings

`RustServer.Snapshots` currently has a **direct WebRCON dependency**. Its API
requires `RUSTSERVER_RCON_URL` and `RustServerRcon:Password`, constructs
`WebRconServerCommandClient` itself, and `SnapshotOperationService` executes
`server.save` for a fast snapshot. The development Snapshots unit injects the
canonical password at process start, so changing only a topology endpoint
would not remove the secret dependency.

`RustServer.Rcon` can technically execute that command on a caller's behalf:
its `RconService.ExecuteCommand` gRPC method (also HTTP-transcoded as
`POST /v1/rcon:execute`) forwards every non-empty command to the direct
WebRCON client. `GetConnectionStatus` is also available. The inspected gRPC
service source has no per-command allowlist or caller-authentication policy;
it is therefore not an approved substitution for Snapshots merely because the
transport exists. Moving Snapshots to it would require an explicit contract
for client authentication, endpoint exposure, command authorization, failure
semantics, generated/client dependencies, and the removal of the direct
password configuration. No such change is made or implied by this audit.

### Conflicts requiring an explicit decision

1. **Missing agreed decision record.** The requested
   `rustserver-runtime-configuration-migration-decision.md` is unavailable.
   Its approved choices must be restored or supplied before a final contract
   reconciliation or implementation begins.
2. **Control's writable state (resolved by Stage 6).**
   `/var/lib/rustserver/control/server-configuration.json` is the approved
   Control-owned state file. Development Bootstrap atomically copies only a
   legacy-only file; an existing canonical file remains authoritative.
3. **Shared directory/access model (resolved by Stage 9).** Both Bootstraps
   ensure the shared directory is `root:root`, mode `0755`; individual files
   retain the least-privilege modes required by their readers. The RCON secret
   remains workspace-owned `0600` for its direct Development consumers.
4. **Installed runtime assets.** Development units and Control still point to
   a checkout/sibling build output, whereas the target requires executable
   runtime assets below `/opt/rustserver/runtime/`.
6. **Updater's coupled paths (resolved by Stage 5).** Production Bootstrap,
   the executor, `RustServer.Updater`, and the credential drop-in now use the
   same flat paths. A host with an installed legacy binary still requires the
   explicit compatible Updater upgrade described below before its active
   readers can switch.
7. **Snapshots-to-Rcon boundary.** Direct RCON is the implemented Snapshots
   design; Rcon gRPC has a command-execution capability but no documented
   caller-authorization boundary for this use. The transport alone does not
   resolve the security and ownership question.

### Repository implementation status after Stages 5, 6, and 7

| Repository | Next implementation responsibility (after the conflicts above are resolved) |
| --- | --- |
| `rust-server-platform` | Stage 5 adds non-destructive migration helpers, moves the Executor and credential drop-in to the canonical files, and documents the explicit compatible-release rollout. Stage 7 adds root-managed installed Compose/script pairs under `/opt/rustserver/runtime/`; legacy values and permissions are retained until cleanup criteria are met. |
| `RustServer.Updater` | Stage 5 changes the registry and release-channel defaults and updates its public documentation and OpenAPI in lockstep with the platform Executor. The API remains unable to own or write the Bootstrap-owned registry. |
| `RustServer.Control` | Stage 6 defaults its atomic JSON store to `/var/lib/rustserver/control/server-configuration.json`; Stage 7 supplies its existing Compose-path options with installed runtime paths without changing handlers or APIs. |
| `RustServer.Rcon` | Implement the selected production password-delivery mechanism without embedding the raw secret in a generated unit. Preserve topology validation and test the selected systemd/file boundary. |
| `RustServer.Snapshots` | Retain direct canonical-password injection unless the Rcon boundary is explicitly approved; a gRPC move would require a supported client plus the security/authorization contract described above. |
| `RustServer.Auth` | Update the service and operator documentation only after the contract decides whether and how its external environment belongs in the flat configuration set. |
| `RustServer.Versions`, `RustServer.Plugins`, `RustServer.Web` | Revalidate topology and installed-runtime integration after platform unit/runtime relocation. No direct application-source relocation was found in this audit. |

### Stage 5 rollout order

1. Publish a `RustServer.Updater` release containing the Stage 5 canonical
   defaults.
2. Run the explicit `upgrade-updater <release-tag>` operation. It copies
   legacy-only files first, retains them, updates the credential drop-in, and
   validates the requested archive embeds the canonical paths. It stages the
   matching Executor and preserves the active Executor before it stops the
   service; it then atomically activates the Executor and Updater under one
   durable transaction. A failed or interrupted transaction restores the
   previous Executor before the previous Updater release is restarted.
3. Do not use an ordinary `install` rerun to upgrade a legacy binary. It stops
   before consumer changes when an existing Updater service or persistent state
   is paired with an active assembly that lacks the canonical paths. A true
   first installation has neither and therefore needs no active release to
   create the canonical files.
4. Validate on a disposable host before separately approving legacy cleanup.

## Current configuration inventory

### Live configuration and secrets

| Current location | Current owner / lifecycle | Readers and writers confirmed in source | Target disposition |
| --- | --- | --- | --- |
| `docker/rust-server/.env` | Git-ignored legacy development configuration. | Step 35 reads it only when the canonical file is absent. Rust/SteamCMD Compose validation, Control's generated environment, steps 93, 95, and 97, and `scripts/dev/run-control.sh` use the canonical file. | Stage 1 copies values atomically and without replacement only when `/etc/rustserver/rust-server.env` is absent. Once present, the canonical file is authoritative; this legacy file is retained only as a recoverable backup. |
| `docker/steamcmd/.env` | Git-ignored legacy local VM file. | No Stage 1 reader uses it. SteamCMD is invoked with `/etc/rustserver/rust-server.env`. | Retain an existing file for rollback only; it is not an active configuration source. |
| `docker/rust-server/.rcon-password` | Git-ignored raw legacy secret. | No active consumer reads it. Step 35 consults it only when `/etc/rustserver/rcon-password` is absent, then copies its value without changing the legacy file. | Retain it as a recoverable backup until cleanup criteria are met. `/etc/rustserver/rcon-password` is the authoritative raw secret, owned by the workspace owner with mode `0600`. |
| `docker/rust-server/server-configuration.json` | Legacy Control-owned desired/effective configuration in the development checkout. | Step 85 reads it only when `/var/lib/rustserver/control/server-configuration.json` is absent, then copies its bytes atomically without changing the legacy file. | Retain unchanged as a recoverable backup; it is not an active Control storage path. |
| `/var/lib/rustserver/control/server-configuration.json` | Canonical Control-owned desired/effective state. | Control defaults to this path; the generated service unit and `run-control.sh` explicitly select it. | Active Stage 6 state file. An existing canonical file is authoritative; Control owns the dedicated state directory and atomically replaces the file on save. |
| `/etc/rustserver/topology.env` | Persistent topology file, initialized create-only from `config/topology.env.example`. | Step 35 writes it; `scripts/lib/topology.sh`, `with-topology.sh`, and `run-control.sh` read it. Generated development units for Control, Rcon gRPC, Versions, Plugins, Snapshots, and Web import it with `EnvironmentFile=`. | Retain in place; update all consumers only as required by the installed-runtime migration. |
| `deploy/bootstrap/bootstrap.env.example` | Version-controlled Production Bootstrap installation defaults. | `deploy/bootstrap/lib/common.sh` parses it as data for each Bootstrap invocation; steps 40, 50, 55, and 80 consume the resolved values. | Retain in the repository and downloaded Bootstrap archive only. Same-named supported environment values apply to the current invocation; no copy is created under `/etc/rustserver/`. |
| `/etc/rustserver/bootstrap.env` | Legacy production Bootstrap configuration from earlier installations. | No active reader after Stage 4. | Preserve unchanged and ignore it; do not delete it as part of this migration. |
| `/etc/rustserver/updater.env` | Create-only Updater listener configuration. | Production step 40 writes it; steps 40, 55, 60, 70, and 80 read it. The Updater systemd unit imports it. | Retain in place. |
| `/etc/rustserver/updater-applications.ini` | Root-owned, group-readable deployment registry. | Step 55 creates it or copies a legacy-only INI; the Deployment Executor and `RustServer.Updater` default to it. | Active Stage 5 registry; existing canonical content is authoritative. |
| `/etc/rustserver/updater-release-channel` | Persistent `Development` or `Production` selection. | Step 55 creates it or copies a legacy-only file; `RustServer.Updater` defaults to it through `ReleaseChannelOptions`. | Active Stage 5 release-channel source. |
| `/etc/rustserver/updater-github-token` | Root-owned Updater credential. | Step 55 creates it or copies a legacy-only credential; step 60 exposes it through `LoadCredential`; `RustServer.Updater` reads systemd's private credential path from `RUSTSERVER_UPDATER_GITHUB_TOKEN_FILE`. | Active Stage 5 token source. |
| `/etc/rustserver/updater/{applications.ini,release-channel,github-token}` | Legacy files from pre-Stage-5 installations. | Migration copies them only when their canonical counterpart is absent; a compatible active Updater never reads them. | Retain unchanged as recoverable backups until separately approved cleanup. |
| `/etc/rustserver/updater/applications.json` | Legacy registry recognized only for one-time conversion. | Step 55 converts it only when both canonical and legacy INI are absent, then preserves a timestamped backup. | Do not treat it as a live target source after cutover. |
| `/etc/rustserver/auth.env` | Installer-provisioned Auth environment, atomically created by Bootstrap only when absent. | The platform Auth unit imports it; the deployment executor validates it before Auth activation. `RustServer.Auth` ships the same path in its unit and deployment documentation. | Active Stage 8 canonical file. Preserve an existing valid file byte-for-byte; do not move, rename, duplicate, or automatically replace credentials. |

### Versioned templates and deployment inputs

These are source templates or deployment inputs, not the live files above.
They must remain templates after migration and must not become another runtime
configuration location.

| Source location | Current use |
| --- | --- |
| `config/topology.env.example` | Step 35 source for the topology file. Its legacy RCON-secret comment is outside this stage's approved file scope. |
| `docker/rust-server/.env.example` and `docker/steamcmd/.env.example` | Development checkout templates. |
| `deploy/bootstrap/bootstrap.env.example` | Version-controlled installation defaults parsed from the downloaded Production Bootstrap archive; never a persistent runtime file. |
| `deploy/config/applications.ini` | Source template for the production application registry. |
| `RustServer.Auth/deploy/auth.env.example` | Auth-only value reference; Production Installer creates the canonical environment from first-run input rather than copying this template. |

### Configuration that is state, not a target `/etc` file

`/var/lib/rustserver/updater/` (operation requests, deployments, staging and
state), `/var/lib/rustserver/auth/auth.db`, and application state such as
Snapshots archives are persistent state, not replacements for the approved
platform files. Release trees under `/opt/rustserver/*/releases` are installed
artifacts, not editable configuration. They must be preserved through the
migration.

## Current systemd and Docker Compose dependencies

| Integration | Current dependency | Migration implication |
| --- | --- | --- |
| `rustserver-control.service` (development) | Generated by step 85. It imports topology and `/etc/rustserver/rust-server.env` with `EnvironmentFile=`, points Control at absolute Compose files below `/opt/rustserver/runtime/`, and points its JSON store at `/var/lib/rustserver/control/server-configuration.json`. | Stage 7 moves all routine Control Compose consumers outside the platform checkout while retaining canonical settings and persistent state. |
| `rustserver-rcon-grpc.service` (development) | Generated by step 86. It imports topology; its startup command reads the canonical password and exports it only to the Rcon process. | Stage 2 uses `/etc/rustserver/rcon-password` directly. The generated unit contains the path and startup logic, never the password value. |
| `rustserver-plugins.service` and `rustserver-versions.service` (development) | Generated by steps 93 and 95. Both import topology and derive Rust paths from `/etc/rustserver/rust-server.env`. | Stage 1 source switch is complete; installed-runtime relocation remains separate work. |
| `rustserver-snapshots.service` (development) | Generated by step 97. It imports topology, derives Rust paths from `/etc/rustserver/rust-server.env`, and reads the canonical RCON password at process startup. | Stage 2 retains Snapshots as a direct RCON reader without changing application code. Its generated unit does not contain the password value. |
| `rustserver-web.service` (development) | Generated by step 94. It imports topology and runs Vite from the checkout. | Keep development behavior working during migration; production Nginx assets remain separately installed under `/opt/rustserver/`. |
| `rustserver-updater.service` and credential drop-in (production) | Step 60 imports `/etc/rustserver/updater.env`, sets the Updater working directory below `/opt/rustserver/updater/current`, and maps `/etc/rustserver/updater-github-token` with `LoadCredential`. | The listener file remains in place; service credential delivery uses the canonical token before the compatible release starts. |
| Deployment Executor and recovery units | Executor's `APPLICATIONS_FILE` is `/etc/rustserver/updater-applications.ini`. Bootstrap step 65 installs its socket/recovery units. | The explicit Updater upgrade installs the Executor before the compatible Updater is switched, so both parse one registry. |
| Rust Docker Compose | `/opt/rustserver/runtime/rust-server/compose.yml` uses its colocated `scripts/start-rust.sh` and `/etc/rustserver/rcon-password` as its secret source; it requires `RUST_SERVER_RCON_PORT` derived from topology. | Stage 7 preserves the relative script mount while moving the full asset pair outside the checkout. |
| SteamCMD and Oxide Compose | Their installed Compose files use colocated relative update scripts and `RUST_SERVER_PATH`; Control receives canonical Rust settings in its environment. | Stage 7 installs both complete asset pairs. SteamCMD continues to read `/etc/rustserver/rust-server.env`; Oxide receives the same `RUST_SERVER_PATH` through Control. |

## Stages 5, 6, and 7 implementation and remaining work

### `rust-server-platform`

- Implemented the non-destructive Production Bootstrap migration for the three
  approved files. It copies only a missing canonical file, preserves the copied
  file's owner and mode, and refuses to overwrite a canonical file created by
  an administrator or concurrent process.
- Implemented the Production Bootstrap, Deployment Executor, credential
  drop-in, installer/runbook, and architecture changes as one release boundary.
  Development Bootstrap, Docker/Compose, Control runtime assets, and unrelated
  configuration remain outside Stage 5.
- Stage 7 adds Development Bootstrap step 82. It installs only the six
  root-managed runtime files under `/opt/rustserver/runtime/`, verifies each
  Compose-to-script relationship statically, and does not invoke Docker or
  alter persistent configuration or Rust data.
- Kept versioned templates in Git and retained the legacy directory unchanged
  as recoverable backup material. Test fixture changes are deliberately outside
  this stage's user-requested scope.

### `RustServer.Updater`

- Updated the default `ApplicationRegistryOptions.StoragePath` and
  `ReleaseChannelOptions.StoragePath` to the flat target files.
- Updated the README, API contract, OpenAPI, and publishing documentation.
  Existing tests remain unmodified by the Stage 5 scope restriction.
- Kept the registry read-only to the Updater and require an explicit compatible
  release before installed Updater and Executor consumers switch.

### `RustServer.Rcon`

- Add or select a supported service-start mechanism that lets the gRPC host
  read the one canonical raw password without exposing it as a literal unit
  environment value. The current host accepts only
  `RustServerRcon:Password`; the platform currently supplies that value from
  the checkout file.
- Test the selected systemd credential/file mechanism and preserve topology
  validation for the RCON and gRPC endpoints.

### `RustServer.Control`

- Defaults, Development Bootstrap, and the direct development launcher use
  `/var/lib/rustserver/control/server-configuration.json`. Bootstrap preserves an
  existing canonical file, otherwise atomically copies the legacy JSON and
  retains it as a recoverable backup. The generated unit gives the Control
  service user ownership of the dedicated state directory required for atomic
  replacement, without modifying `/etc/rustserver`.
- The generated service and direct launcher use absolute Compose paths below
  `/opt/rustserver/runtime/`. Docker Compose resolves each retained relative
  script mount from its Compose-file directory, not the Control process
  working directory. Control's existing lifecycle, update, Oxide, wipe, and
  startup-log handlers therefore retain their orchestration and APIs.
- Keep RCON access through the gRPC host and validate lifecycle, update, Oxide,
  wipe, and startup-log paths on a disposable VM after the Compose relocation.

### Other affected repositories

- `RustServer.Snapshots`: Stage 2 retains its direct
  `RustServerRcon__Password` integration and supplies the existing raw secret
  from the canonical file at process startup. Moving Snapshots to the Rcon
  gRPC contract remains future application work.
- `RustServer.Auth`: its checked-in unit and production documentation use the
  Stage 8 canonical `/etc/rustserver/auth.env`; Installer/Bootstrap own only
  first-run provisioning, while release artifacts never carry credentials.
- `RustServer.Versions`, `RustServer.Plugins`, and `RustServer.Web`: no direct
  old configuration path was found in their application sources during this
  review. Their platform-generated units still need integration validation for
  topology, `/srv/rust/server`, and installed runtime assets. `RustServer.Web`
  also needs development/production documentation kept distinct.

## Safe migration order

1. Resolve the open architecture questions below and record the decisions in
   the canonical document or an approved successor. Do not implement an
   implicit location for missing target data.
2. Re-inventory the exact application releases, installed systemd units,
   Compose projects, and host-owned overrides. Back up metadata and values
   without printing secrets. Identify an old/new conflict before changing any
   consumer.
3. Add automated migration helpers and tests. For every approved target file,
   create it only when missing and copy the existing value verbatim. If both
   locations exist but differ, stop with a diagnostic and leave both intact.
4. Install the complete Docker Compose/runtime asset set under
   `/opt/rustserver/runtime/` and validate it with the target configuration
   before redirecting Control or systemd units.
5. Release compatible application and platform changes together: Updater and
   Executor registry readers, Rcon credential loading, Control's configuration
   integration, and any approved Snapshots/Auth changes. Preserve existing
   releases, state, and service identities.
6. Switch systemd and Compose consumers to the target files, then restart only
   the affected services through an explicit, ordered procedure. Keep old files
   as read-only rollback material until the full validation matrix passes.
7. Validate both development and production flows on a disposable host,
   including removal or renaming of the Bootstrap checkout. Only then perform
   the controlled cleanup described below.

## Validation requirements

- Verify values, line endings, mode, owner, group, and regular-file/no-symlink
  checks for each migrated target. Confirm the RCON password remains a single
  non-empty raw line and is never logged.
- Exercise missing-target creation, legacy-value preservation, rerun
  idempotency, and concurrent creation for every migration path. For the RCON
  password, an existing canonical file is authoritative and is neither
  compared with nor replaced by the legacy file.
- Render/validate all Compose projects using the installed files and target
  configuration. Confirm Rust, SteamCMD, and Oxide use `/srv/rust/server` and
  that the Rust secret mount resolves to `/etc/rustserver/rcon-password`.
- Inspect generated units with `systemctl cat`; prove no active unit, drop-in,
  or `ExecStart` points into a checkout or the removed updater subdirectory.
  Verify service users can read only the settings they require.
- Exercise Rust lifecycle, update, Oxide update, wipe, startup-log tracking,
  WebRCON/Rcon gRPC, and the Control API. Test RCON password rotation with both
  approved consumers.
- Exercise Updater discovery, refresh, Prepare, Install, Update, Rollback,
  self-update, transaction recovery, and credential access. The Updater and
  Executor must parse the same target registry and preserve release-channel
  behavior.
- Start Versions, Plugins, Snapshots, Web, Auth (when its decision is made),
  and verify their topology and persistent-state integrations. Validate that a
  fast snapshot still reaches RCON through the approved path.
- Run the repository test suites affected by the migration and a disposable-VM
  smoke test. Rename or remove the Bootstrap/source checkout only after the
  installed services have started successfully.

## Cleanup criteria

Cleanup is a separate, post-validation operation. It may remove an obsolete
configuration copy or compatibility path only when all of these are true:

- The destination file exists, matches the approved/migrated value, and has
  the required ownership and permissions.
- All active units, Compose definitions, application defaults, executor code,
  and operator documentation have been switched and verified.
- Development and production validation, rollback, and a checkout-removal test
  have passed on representative hosts.
- A recoverable backup and a documented rollback procedure exist; removing an
  old file cannot discard the only copy of a secret or administrator setting.
- Historical changelog entries and migration backups required for audit are
  retained. The legacy JSON registry and timestamped INI backups follow an
  explicit retention policy rather than an unconditional recursive delete.

Do not delete `/etc/rustserver/bootstrap.env`, `/etc/rustserver/updater/`,
checkout `.env` files, the old RCON secret, or Control's JSON store merely
because target files have been created. Those paths are obsolete only after the
criteria above are met.

## Architecture questions to resolve before implementation

1. Stage 2 explicitly retains Snapshots as a direct reader of the canonical
   RCON password without application changes. A future application stage may
   migrate it to the Rcon gRPC contract.
2. Stage 2 makes `rcon-password` owned by the workspace owner with
   mode `0600`; root and the Rcon and Snapshots services can therefore read it.
   Their generated units read it at process startup and never embed its value.
   Final ownership and systemd credential strategy remain to be specified for
   the other secrets, including the Updater token.
