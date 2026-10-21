# Iteration 044 — Plugins platform integration and release readiness

## Goal

Make the rebuilt RustServer.Plugins compatible with the current platform
Bootstrap and systemd deployment model without introducing a new business
feature, public-hosting configuration, or a second topology source.

## Added and updated

- PluginManagement Source workspaces still clone `develop` initially, while an
  existing clean workspace may safely remain on `develop` or
  `release/<version>` when it tracks the matching canonical `origin` branch.
  Unsupported, dirty, ahead, diverged, detached, or incorrectly tracked
  workspaces fail without automatic repair.
- Bootstrap stops an active `rustserver-plugins.service` before synchronizing
  the shared Configs and Plugins Source workspaces, and rechecks inactivity
  before the nested workspace mutation. Step 93 starts or restarts the service
  after the pipeline completes; a later failure deliberately leaves it stopped
  until the operator corrects the issue and reruns Bootstrap.
- The Plugins installer now validates effective read/write/traverse access for
  both Source workspaces and their Git metadata, verifies that the service user
  can run Git in each workspace, and explicitly requires the Git executable.
- The Plugins solution now includes the committed OpenAPI generator. The build
  step therefore restores and builds it with the solution before running its
  non-writing `--no-build --no-restore --verify` mode.
- Bootstrap and service documentation now describe Source workspace ownership,
  persistent Environment/Baseline state, runtime-path mapping, 404 readiness,
  and unchanged centralized topology accurately.

## Current capability

The existing platform deployment can build, validate, install, and restart the
Plugins service with its persistent state, Rust runtime mapping, Control
dependency, and writable Git Source workspaces intact. A Bootstrap rerun does
not force a selected Release workspace back to Development or silently change
the server Runtime/Baseline after a workspace-only fast-forward.

## Deliberately deferred

- Public Internet hosting, reverse proxying, authentication, HTTPS, firewall
  configuration, and the platform network-isolation TODO remain separate work.
- Bootstrap does not copy SSH keys, create credentials, or add a distributed
  lock; it relies on the non-root workspace owner and explicit service stop.

## Validation

- RustServer.Plugins Release build passed with zero warnings/errors; its full
  Release suite passed 45 tests; and the OpenAPI generator `--verify` passed.
- An isolated NuGet package cache verified the cold sequence: solution restore,
  solution Release build and test without restore/build, then generator
  `--no-build --no-restore --verify`.
- Updated Bootstrap shell scripts passed `bash -n` static syntax validation.
- An isolated temporary-Git run verified initial `develop` clone, safe
  `release/2.0` fast-forward, and rejection of dirty, ahead, diverged,
  wrong-upstream, and unsupported-branch workspaces.
- No full Ubuntu/systemd Bootstrap was run from the Windows development
  workspace; systemd installation, service-user access, and remote Git access
  were reviewed statically against the scripts and remain host-level checks.

## Decisions

- `/etc/rustserver/topology.env` remains the single runtime topology source;
  `RUSTSERVER_PLUGINS_URL`, `RUSTSERVER_PLUGINS_BIND_HOST`, and
  `RUSTSERVER_CONTROL_URL` stay externally supplied rather than hardcoded.
- Plugins retains its current Feature-Based Architecture with command handlers,
  orchestration, and ports/adapters. This iteration changes deployment
  integration only.
