# Iteration 088 — Plugins GitHub credential transport

## Goal

Begin the migration of `RustServer.Plugins` from separately configured SSH
authentication to a dedicated HTTPS GitHub credential, while retaining
existing Git workflows and the current Bootstrap until the remaining stages
are verified.

## Added and updated

- `RustServer.Plugins` now uses a credential-aware Git CLI transport for
  `fetch` and `push` against the pinned HTTPS GitHub URLs of
  `RustServer.PluginFiles` and `RustServer.ConfigFiles`.
- `IGitHubTokenProvider` (added previously) reads a protected systemd
  credential file; a short-lived Git askpass helper accesses that file
  without embedding its value in command lines, Git configuration or an
  environment variable.
- Added regression tests for the askpass handshake, unexpected HTTPS origin
  rejection and existing filesystem-origin compatibility.
- Updated Plugins requirements, architecture and README to distinguish the
  implemented transport from pending deployment integration.

## Current capability

- Existing Git CLI Fetch / Commit + Push commands are unchanged for local
  and SSH origins.
- Canonical GitHub HTTPS origins use the Plugins credential when the service
  receives `RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE` pointing at a readable
  protected token copy.
- Noncanonical HTTPS origins are rejected rather than receiving the token.

## Deliberately deferred

- Plugins' automatic creation/clone of missing Git workspaces.
- Bootstrap's dedicated `/etc/rustserver/plugins-github-token` provisioning
  and systemd `LoadCredential` wiring.
- Migration of existing SSH origins, removal of Bootstrap workspace sync
  steps 61 and 62, and later removal of obsolete instructions.
- End-to-end tests against a live GitHub remote and production installer.

## Validation

- GitHub commits and re-read of changed source completed.
- The .NET SDK is not available in the current execution container; the
  Plugins test suite and solution build have **not** been executed here.
- Bootstrap remains unchanged in this iteration.

## Decisions

- Retain Git CLI; credentials change the transport rather than Git semantics.
- Restrict the Plugins runtime token to read/write operations on the two
  dedicated source repositories. Keep Updater's credential independently
  replaceable, and preserve existing secret values on re-install.
