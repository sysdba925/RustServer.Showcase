# Updater ownership and release model

The API serves the standalone console at `/emergency` and local assets under
`/console/assets/`; they ship in the paired release bundle. The browser uses
existing durable Prepare and Install operations for initial rollout, with no new
privileged endpoint or store. Operation stages reflect the latest reported step,
not a persisted timeline; in-page alerts are separate from durable records.

`RustServer.Updater` owns the deployment executor, deployment transactions,
self-update, and recovery. They are one runtime product: no Bootstrap or
Installer executable is consulted after initial provisioning or migration.

## Paired release bundle

Every Updater GitHub release contains exactly one bundle layout:

```text
rustserver-updater.bundle.json
api/RustServer.Updater.Api.dll
executor/RustServer.Updater.Executor.dll
systemd/rustserver-updater.service
systemd/rustserver-updater-executor.socket
systemd/rustserver-updater-executor@.service
systemd/rustserver-updater-recovery.service
```

The manifest binds those two entrypoints to the release tag. Package staging
rejects a self-update bundle if either entrypoint is missing or the manifest
version/layout differs. The publishing script refuses to create a release
without the API, executor, and all four systemd resources.

Each installed Updater release directory is one bundle and `current` is a
single symbolic link to that directory. A self-update copies and validates the
complete staged bundle, changes `current` once, then restarts the Updater.
Therefore the API and the executor always resolve through the same release
link. On an activation or readiness failure, the executor restores the prior
link and the prior unit resources before restarting the prior release. The
recovery unit repeats that rollback for a durable non-terminal transaction.

## Deployment and systemd policy

The unprivileged API writes a request named by a UUID and sends only that UUID
to the executor socket. The root executor rereads the Updater runtime registry
at `/etc/rustserver/updater/applications.ini`, verifies the staged manifest,
holds `/run/lock/rustserver/updater.lock`, and persists a transaction under
`/var/lib/rustserver/updater/deployments/` before reporting an outcome. A
Prepare request may additionally copy the verified link-free package to the
root-owned `/opt/rustserver/<application>/prepared/<tag>/` handoff, removing
group/other write access. It does not inspect, execute or provision an
application artifact, and it never changes `current`, a release link or
systemd state.

Ordinary deployment always starts a service with `systemctl start <unit>`.
`--ignore-dependencies` is available only through the executor's
`SelfUpdateOrRecovery` path; that path is used by a self-update or crash
recovery, never by an ordinary install, update, or rollback.

## Control-owned NetworkAccess helper activation

The verified `RustServer.Control` release also contains
`network-access/systemd/rustserver-network-access-helper.service` and its
fail-closed nftables guard. The helper is a separate root-owned **process**,
not a separately versioned application. The Control API remains non-root.

On Control install, update, rollback, or crash recovery, the privileged
Executor validates the fixed helper artifacts and installs the single
reviewed `rustserver-network-access-helper.service` symlink, pointing into
Control's stable `current` release. It also installs a fixed Docker systemd drop-in which invokes the release-owned
fail-closed nftables guard in Docker's `ExecStartPre` step. This blocks Docker
startup if the guard fails, without making Docker's runtime dependent on a
continuously running Helper. The drop-in remains managed by the Executor,
not Bootstrap or the Control API. It reloads systemd
**after** selecting the Control release and **before** starting Control. The normal
`Requires=/After=` dependencies in `rustserver-control.service` cause
systemd to start the helper with its own restricted privileges.

On successful readiness, the Executor enables the helper and Control for
boot. On first-install failure, the Executor stops and disables the helper,
removes its registration and Docker's firewall-guard drop-in; on an update failure it restores the prior
release, reloads the unit, and restarts the prior API with normal systemd
dependencies. This also applies to recovery: Control never bypasses its
helper dependency. The helper's enabled unit runs the fail-closed guard
before Docker starts on subsequent boots.

Bootstrap installs the nftables prerequisites and the non-root Control
account/unit, but does not register a dangling link to an as-yet-uninstalled
Control release. The Updater API cannot change helper paths or unit names
through HTTP; the root executor operates on only this fixed unit for the
registered Control application.

## Provisioning and migration

An existing Bootstrap may install the first complete bundle and its systemd
units, and may migrate the legacy registry to the runtime path. It must not
perform subsequent Updater upgrades, run the executor, or participate in the
runtime lock. Once the bundle is installed, all updates and recovery are
performed solely by `RustServer.Updater.Executor`.

## First-install hook diagnostics

The Executor supplies a private diagnostic file through `RUSTSERVER_PROVISIONING_DIAGNOSTIC`. The verified hook may write one UTF-8 JSON object, at most 1024 bytes, with `version: 1` and bounded lowercase identifier strings `stage`, `category`, and `reason`. These are owner-defined, fixed, non-secret codes, never command output or configuration values. The Executor validates the complete record and records its codes and the process exit code in the durable error; missing or invalid records become stage `unknown` and category `unknown`. Bounded, redacted stdout and stderr are retained separately for failed hooks and are available automatically through the operation API. Existing hooks without this optional protocol remain compatible.

First-install hook output is captured asynchronously with a bounded size and retained with the executor transaction and operation. Common credential assignments and known secret environment values are redacted; arbitrary output may still contain secrets. The API exposes diagnostic output automatically; the listener and any reverse proxy require a trusted administrative access boundary. The Updater does not authenticate callers. Operation.Error remains a safe classification.
