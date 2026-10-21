# Iteration 008 — Reproducible Ubuntu bootstrap

## Goal

Provide a single, ordered bootstrap command for a freshly cloned `rust-server-platform` repository on Ubuntu 24.04:

```bash
./scripts/bootstrap.sh
```

The bootstrap prepares the development/server host without provisioning a VM, managing GitHub credentials, installing Rust Dedicated Server content, starting services, or changing the separately versioned application repositories outside their normal Git synchronization.

## Changed components

- `scripts/bootstrap.sh` ordered fail-fast runner
- Numbered bootstrap steps `10` through `99` under `scripts/bootstrap.d/`
- Generic helpers in `scripts/lib/common.sh` and safe Git synchronization in `scripts/lib/git.sh`
- Ubuntu development-tool installation for .NET SDK 10, Docker Engine/Compose, lazygit, lazydocker, TermIDE, `mc`, and CLI utilities
- Canonical workspace layout with `repos/`, `packages/nuget/`, `packages/global-packages/`, workspace `NuGet.config`, and `/srv/rust/server`
- Local Docker configuration creation from `.env.example` and local `.rcon-password` generation/preservation
- SSH-based synchronization of `RustServer.Rcon`, `RustServer.Control`, and `RustServer.Versions`
- RCON package build/test/pack, Control build/test, and Versions build/test steps
- Workspace-local NuGet cache support, deterministic internal package consumption, and final host/tool validation
- `scripts/dev/run-control.sh` support for the non-secret RCON port from `.env`
- Bootstrap documentation in `scripts/bootstrap/README.md`, `OVERVIEW.md`, and `EXTENDING.md`

## Bootstrap behavior

The runner discovers numbered scripts, executes them in numeric order, preserves child output, and stops at the first non-zero exit code. A valid already-existing state is a logged successful no-op.

The current sequence is:

```text
10  prerequisite checks
20  development tools
30  workspace and runtime directories
35  local configuration and RCON secret
40  RustServer.Rcon repository sync
50  RustServer.Control repository sync
60  RustServer.Versions repository sync
70  RustServer.Rcon restore/build/test/pack
80  RustServer.Control restore/build/test
90  RustServer.Versions restore/build/test
99  final host/tool validation
```

Each numbered script declares the paths, inputs, actions, and validations it owns. `bootstrap.sh` remains only an ordered runner. Adding a repository or build operation normally means adding one self-contained numbered script rather than extending shared registries or unrelated validators.

## Decisions

- The canonical workspace is the parent of `rust-server-platform`: dependent repositories live in `repos/`; local packages live in `packages/nuget/`; the NuGet global-packages cache lives in `packages/global-packages/`.
- The established Rust runtime remains `/srv/rust/server`; bootstrap creates it when absent and does not delete or recursively alter existing server data.
- Step 10 validates Ubuntu 24.04, required local commands, a platform Git worktree with `origin`, and passwordless `sudo`; dependent repository access is checked by the repository synchronization steps themselves.
- Step 20 installs from official Microsoft and Docker repositories where applicable, uses verified official release assets for standalone terminal tools, refuses to silently replace conflicting Docker packages, and warns when a new login is required for Docker group membership.
- `.env` remains the versioned-template-derived, local non-secret configuration. It is never sourced as shell code by `run-control.sh`.
- `.rcon-password` remains the sole local RCON secret. It is generated only when absent, is never printed, is preserved on reruns, and is checked with mode `0600`.
- `run-control.sh` resolves the RCON port in this order: `RustServerRcon__Port`, exported `RUST_SERVER_RCON_PORT`, `RUST_SERVER_RCON_PORT` from `.env`, then `28016`.
- New dependency clones use SSH URLs. Existing HTTPS origins for the same GitHub repositories remain accepted through generic URL normalization.
- Git synchronization never uses `git reset --hard`, `git clean`, or destructive repository replacement. Dirty worktrees, invalid origins, detached HEAD, invalid upstreams, and non-fast-forward updates fail safely.
- `RustServer.Rcon` package ID and version are read from project metadata. Only its matching `.nupkg` and exact workspace-cache entry are replaced; the user-global NuGet cache is not cleared.
- Control forces restore from the workspace feed/cache after the RCON package is rebuilt and verifies extracted package metadata before build/test.
- Shared code is restricted to generic logging/error/file-command checks and generic safe Git operations. Step-specific `.env`, NuGet, repository, and validation logic stays local to its owner.

## Current capability

The platform can bootstrap a clean Ubuntu 24.04 development/server VM after Git, OpenSSH/GitHub access, and passwordless `sudo` are prepared manually. It creates or preserves local runtime configuration and secrets, prepares the canonical workspace, synchronizes internal repositories safely, builds the internal RCON NuGet package and consumers, and validates the resulting host tooling.

The overview and extension guides provide a one-minute map and a practical contract for adding future sync, build, or NuGet-producing steps without modifying the runner or creating hidden cross-script dependencies.

## Validation

Shell syntax and Git diff checks were run during implementation. Existing HTTPS origins were verified as matching the configured SSH canonical repository URLs. Local `.env` and `.rcon-password` remain Git-ignored and no secret values were added to source or logs.

No full Ubuntu 24.04 bootstrap, tool installation, repository synchronization, or .NET build/test/pack smoke test was run from the Windows development host.

## Out of scope

- VM, cloud, Terraform, or VPS provisioning
- Git installation, SSH-key generation, GitHub authentication, commits, pushes, or tags
- Rust Dedicated Server installation/update and automatic service startup
- Oxide/uMod, plugins, monitoring, CI/CD, remote NuGet registries, databases, Redis, or UI
