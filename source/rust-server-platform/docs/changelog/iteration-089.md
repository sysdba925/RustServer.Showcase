# Iteration 089 — Plugins-owned Git initialization and credentials

## Goal

Complete stages 03 and 04 of moving PluginFiles/ConfigFiles ownership from
Bootstrap to RustServer.Plugins, without removing existing Bootstrap Git sync
or migrating existing SSH checkouts prematurely.

## Added and updated

- Plugins feature initializes missing Configs and Plugins Git workspaces,
  including the nested development layout and empty production paths.
- Clone operates through existing GitProcess with the Plugins token and HTTPS
  GitHub origins, using a temporary sibling directory followed by move.
- Existing repository roots and origin are checked; checked-out branches,
  local commits, runtime files, and uncommitted changes are preserved.
- Host startup uses the existing exclusive Git mutation guard and requires a
  stopped Rust server before initialization.
- Production Bootstrap provisions root-owned mode 0600
  `/etc/rustserver/plugins-github-token` and a
  `rustserver-plugins.service.d/credentials.conf` systemd drop-in with
  `LoadCredential` and the private credential-copy path.
- Installer optionally accepts a dedicated Plugins token; without one,
  Bootstrap copies the existing Updater token *value* into the independent
  Plugins token file. Reruns validate and preserve the existing file.
- Development Bootstrap conditionally provisions the independent token and
  writes equivalent systemd configuration, retaining existing SSH
  workspaces if no token is available.
- Added focused Git workspace initialization tests and a Bootstrap
  credential-source/shell-syntax regression test. Updated relevant docs.

## Current capability

- Bootstrap supplies filesystem locations and credentials; Plugins has the
  code to initialize missing source workspaces when the systemd
  `PluginManagement__WorkspaceInitialization__Enabled=true` setting is active.
- Production Bootstrap creates the separate Plugins token file (read/write
  GitHub permissions must be assigned to the *actual* token).
- Existing Git workspaces are not automatically modified or reset.

## Deliberately deferred

- Stage 05: migrate pre-existing source workspaces from SSH to HTTPS.
- Stage 06: remove Development Bootstrap legacy source Git sync steps 61/62,
  and its precloned-workspace validation.
- Stage 07: run full build, .NET tests, end-to-end GitHub and clean VM
  integration tests.
- Stage 08: remove all superseded documentation and dead code.

## Validation

- Relevant GitHub source and docs updates have been committed.
- Automated .NET builds/tests and live VM end-to-end behavior were **not**
  executed in this tool environment (no dotnet SDK / VM access).
- Static Bootstrap credential contract tests have been added, but were not
  executed in this environment.

## Decisions

- Existing protected token files are never overwritten by repeat Bootstrap.
- The token files are independent even when they temporarily store the
  same token value.
- No production Git clone belongs to Bootstrap. Ownership migration of
  existing Development clones is a separately tested operation.
