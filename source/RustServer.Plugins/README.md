# RustServer.Plugins

`RustServer.Plugins` manages Rust server plugin/configuration files using
two source repositories: `sysdba925/RustServer.PluginFiles` (Plugins) and
`sysdba925/RustServer.ConfigFiles` (Configs).

**Implementation status:** The service is being rebuilt. The architecture
and behavioral specifications in [`AGENTS.md`](AGENTS.md),
[`docs/requirements.md`](docs/requirements.md),
[`docs/operations.md`](docs/operations.md) and
[`docs/architecture.md`](docs/architecture.md) remain authoritative.
The credential and workspace initialization policy below is an approved
**target design**, not a claim that Bootstrap and the application already
implement it.

## GitHub token: purpose and scope

Plugins needs GitHub **read and write** access to these two repositories only:

- `sysdba925/RustServer.PluginFiles` — clone, fetch, commit and push.
- `sysdba925/RustServer.ConfigFiles` — clone, fetch, commit and push.

The intended fine-grained token has **Contents: Read and write** limited
to these repositories (and the minimum GitHub-required metadata access).
It is a runtime service credential, **not** a GitHub Actions secret and
not the Updater credential.

| Runtime service | Credential source on host | GitHub access |
| --- | --- | --- |
| `rustserver-plugins.service` | `/etc/rustserver/plugins-github-token` | Read/write only to the two repositories above |
| `rustserver-updater.service` | `/etc/rustserver/updater-github-token` | Read-only access to private GitHub Releases; no Git push |

Both host-side files must be separately maintained, owned by `root`, and
mode `0600`. Bootstrap must provide Plugins' existing, externally supplied
token through **systemd `LoadCredential`**, not through an origin URL,
`.git/config`, environment-variable plaintext, command-line arguments,
logs, or repository files. The service reads systemd's protected credential
copy and uses it for Git authentication without persisting it to the
workspaces.

**Initial provisioning may put the same token value in both files.**
Their names and lifecycles remain independent; a future separate Plugins
write-capable token can replace only
`/etc/rustserver/plugins-github-token` without changing the Updater
credential or either application. Until separate tokens are issued,
GitHub itself still grants the shared token's actual permissions, even
though the Updater only performs reads.

## Credential reader (implemented)

The `PluginManagement/Git` capability contains `IGitHubTokenProvider` and
`SystemdGitHubTokenProvider`, registered through DI. On demand, the provider
reads the protected credential file whose path is set by
`RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE`. A missing, unreadable, or empty file
returns a non-secret diagnostic. Local-only requests and application startup
do not need the credential.

**Stage 02 implemented (pending test execution):** `RemoteGit` now uses the
credential-aware `GitProcess` for Fetch/Push when `origin` is one of the
two canonical GitHub HTTPS repository URLs. Git invokes an ephemeral askpass
helper; the token is read directly from the systemd credential copy, not
stored in Git configuration or command arguments. Unrecognized HTTPS origins
are rejected. Existing SSH and filesystem remotes retain their behavior.

**Current status:** Bootstrap now provisions the dedicated Plugins
credential and `LoadCredential`; Plugins can initialize missing workspaces.
Existing SSH workspaces are deliberately not migrated yet (Stage 05), and
the old Development Bootstrap clone/sync steps remain (Stage 06). No full
.NET build or live GitHub/VM integration run has been completed here.

## Stage 03 — automatic workspace initialization

The Plugins host has an optional startup initializer controlled by
`PluginManagement__WorkspaceInitialization__Enabled=true`. Both Bootstrap
systemd installations enable it. It checks Configs first, Plugins second,
creates missing parent folders and clones the canonical `develop` branch
using HTTPS + its private credential. It recognizes Bootstrap-created empty
directories and fills them safely. Clone data is built in a temporary sibling
directory and moved to the destination only after success.

An existing Git checkout is **never reset, re-cloned, or automatically
fast-forwarded** at startup. A non-empty non-repository directory, wrong
origin, or unsafe checkout causes a diagnostic failure and leaves the
operator's files untouched. Startup remains available for local status APIs;
an initialization failure is logged without secret details and can be retried
by restarting the service after fixing the underlying condition.

## Stage 04 — Bootstrap credential provisioning

Production Bootstrap independently provisions
`/etc/rustserver/plugins-github-token` with `root:root` mode `0600`.
The Installer optionally accepts `PLUGINS_GITHUB_TOKEN` or
`PLUGINS_GITHUB_TOKEN_FILE`; if neither is supplied on first provision,
Bootstrap copies the already-provisioned Updater token **value** into a
separate Plugins file. The value must have actual GitHub read/write access
for Plugins pushes. Existing Plugins token files are preserved, validated,
and never rotated by reruns.

systemd `LoadCredential` exposes only its private credential copy to Plugins
via `RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE`. The production unit gets a
managed credentials drop-in; Development's generated unit uses the same
mechanism when a Plugins token or an existing Updater token is available.
Development without either token preserves legacy SSH workspaces until
the explicit migration. A dedicated token can later be rotated independently.

## Git workspace ownership (target design)

Plugins, not Bootstrap, initializes and maintains the working copies of
`RustServer.PluginFiles` and `RustServer.ConfigFiles`:

1. On first startup, clone a missing workspace and initialize the
   `develop` working branch for Development.
2. On subsequent startups, reuse existing clones without resetting
   local changes or recloning.
3. Use the Plugins credential for authorized fetch/push operations.
4. Report missing permissions, invalid remotes or unsafe local Git state
   explicitly; never silently repair or force-reset a workspace.
5. Respect the selected Environment/Source (Development or
   `release/<version>`); do not switch Production to `develop` or
   enable Production commits.

Deployment Bootstrap is responsible for **configuration, workspace paths,
credentials and systemd integration**, not Plugins' Git operations.
The existing Development Bootstrap still executes legacy steps 61/62
for ConfigFiles and PluginFiles. They will be removed in Stage 06, after the
Stage 05 migration and integration tests. Stages 03 and 04 described above
are **implemented in source**, but the full deployment transition is not
complete until those follow-up stages are verified.

The dedicated Updater credential and its existing `LoadCredential` flow
are documented in the
[`RustServer.Updater` README](https://github.com/sysdba925/RustServer.Updater/blob/develop/README.md#github-authentication).
