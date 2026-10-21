# Iteration 022 — Rust server startup tracking

## Goal

Expose compact, deterministic startup progress for the active Rust Dedicated
Server run without changing lifecycle, WebRCON readiness, or update-operation
semantics.

## Added and updated

- RustServer.Control now exposes `GET /api/server/startup/schema` for the
  fixed, ordered nine-milestone presentation schema and
  `GET /api/server/startup` for a complete current-run snapshot.
- Snapshots represent every milestone as `pending`, `reached`, or `skipped`.
  `SteamServer Connected` is the only authoritative completion signal and is
  surfaced as the server-side `started` value.
- Startup sessions use a deterministic opaque identifier derived from the full
  Docker container ID and `State.StartedAt`; update operation IDs and raw Docker
  IDs are not exposed.
- Docker startup logs are isolated to the active run with `--timestamps` and
  `--since <StartedAt>`. stdout and stderr records are timestamp-merged before
  milestone analysis, and a run change during collection is retried once.
- Startup state is request-driven and reconstructed from retained Docker logs,
  so a restarted Control process can return the same active-run snapshot
  without Control-owned persistent state.

## Deliberately deferred

- Startup-history persistence, log streaming/viewing, live console, SignalR,
  SSE, health scoring, error/warning analysis, and plugin-health checks.
- Public Docker IDs, log timestamps, matcher expressions, and extracted
  engine/Oxide/bootstrap values.

## Validation

- RustServer.Control solution build completed with no warnings or errors.
- All 118 RustServer.Control tests passed.
- Generated OpenAPI artifacts were regenerated and API-contract verification
  completed successfully.

## Limitation

- Docker must retain the active container's output from `State.StartedAt` for
  complete reconstruction after a Control restart. A successful but truncated
  Docker log history cannot be distinguished from an incomplete startup by the
  compact three-state API.
