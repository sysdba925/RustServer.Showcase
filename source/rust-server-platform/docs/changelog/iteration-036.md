# Iteration 036 — Centralize service topology

## Goal

Make one non-secret runtime environment file the source of truth for all
RustServer platform service and WebRCON endpoints.

## Added and updated

- Added the versioned `config/topology.env.example` template and Bootstrap
  installation of `/etc/rustserver/topology.env` without overwriting an
  existing user configuration.
- Added `EnvironmentFile=/etc/rustserver/topology.env` to the Control,
  Versions, Plugins, Snapshots, and Web systemd units.
- Made Control, Versions, Plugins, and Snapshots require their own service URL
  from topology. Plugins and Snapshots require Control's URL; Control,
  Versions, and Snapshots require the verified `ws://` WebRCON endpoint.
- Removed service endpoint defaults from backend settings, launch profiles,
  Bootstrap units, the Control development runner, and the Vite proxy.
- Made Vite bind and proxy from topology at startup. The browser still uses
  stable same-origin proxy paths while the development Vite server is running.
- Made all five HTTP applications bind on `0.0.0.0` while deriving their ports
  from topology URLs, so trusted development-network clients can access them
  through the VM IP without changing service-to-service loopback URLs.
- Added the safe `scripts/dev/with-topology.sh` environment loader for direct
  `dotnet run`, published DLL, and Vite commands.
- Derived Docker Compose's internal RCON port from the WebRCON URL only inside
  Control's process invocation; `docker/rust-server/.env` no longer owns a
  platform endpoint.

## Current capability

An operator can change an endpoint in `/etc/rustserver/topology.env` and
restart the owning service plus its consumers without recompiling a backend.
Bootstrap reruns retain the customized topology file and regenerate units that
reference it.

## Deliberately deferred

- Production static frontend hosting remains unimplemented. A static build
  needs build-time `VITE_*_API_BASE_URL` values and appropriate backend CORS;
  no frontend runtime-configuration service was added.
- WebRCON remains a Rust Dedicated Server WebSocket endpoint, not a platform
  HTTP service or a systemd unit.
- Production network isolation remains unimplemented. The Bootstrap guide now
  records that only Web should eventually be public, with reverse proxy,
  authentication, HTTPS, and firewall controls.

## Validation

- Debug builds succeeded with zero warnings and errors for RustServer.Rcon,
  RustServer.Control, RustServer.Versions, RustServer.Plugins, and
  RustServer.Snapshots.
- Test suites passed: Rcon 11, Control 138, Versions 42, Plugins 24, and
  Snapshots 3.
- Control, Versions, and Snapshots OpenAPI artifact verification passed.
- Web lint, typecheck, and test passed (23 files / 180 tests); the existing two
  `react(set-state-in-effect)` lint warnings remain. A topology-configured
  production Vite build passed.
- `bash -n` passed for the topology helper, direct runner, and all changed
  Bootstrap scripts. The helper exported the complete example topology and
  derived the expected WebRCON port.
- A direct Plugins startup without topology failed with the expected clear
  `RUSTSERVER_PLUGINS_URL` requirement error.
- No live systemd/Docker deployment was available for an end-to-end service
  restart or Bootstrap rerun.

## Decisions

- Topology uses complete URLs. WebRCON is explicitly `ws://` because the
  existing RustServer.Rcon implementation opens a WebSocket connection and
  does not implement TLS (`wss://`).
- Service topology remains a file consumed directly by systemd and process
  configuration. No registry, daemon, generated configuration layer, or
  runtime frontend-configuration endpoint was introduced.
