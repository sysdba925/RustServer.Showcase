# Startup Tracking design

## Status of this document

This is the approved design for the Startup Tracking capability. The initial
implementation now follows it; `openapi.json` and `docs/api-contract.md` are
the authoritative generated public contract.

## Purpose and scope

Startup Tracking gives a Web UI a compact, ordered view of one Rust server
run's progress through known startup-log milestones. It is intended solely for
startup visualisation and polling.

It is not a health check, error or warning analyser, plugin-health check, log
viewer, live console, or log-streaming API. It has no persistent history.

## Existing behaviour that remains unchanged

The following endpoints retain their current routes, payloads, and semantics:

- `GET /api/server/status`
- `POST /api/server/start`
- `POST /api/server/stop`
- `POST /api/server/restart`
- `POST /api/server/update`
- `POST /api/updates/oxide`
- `GET /api/operations/{id}`

In particular, `ServerStatus.isRunning` remains the Docker container lifecycle
state and `ServerStatus.isReady` remains a WebRCON readiness probe. Startup
Tracking neither extends nor reinterprets either flag, and it makes no WebRCON
calls. Update `operationId` values remain limited to update operations and are
never used as startup-session identifiers.

## Schema and baseline milestones

Schema version `1` has exactly these nine entries in this order. The list is a
static application definition: it does not vary during the lifetime of one
Control process. Matchers are internal implementation detail and are never
returned to clients.

Before matching, the log source removes its own Docker `--timestamps` prefix
and line terminator. Literal matching below means a case-sensitive ordinal
substring match on that normalized log line. The two regular expressions are
case-sensitive and culture-invariant.

| Position | ID | Label | Match rule | Future-only extracted value |
| --- | --- | --- | --- | --- |
| 0 | `engine` | Unity Engine | `^Initialize engine version:\s*(.+)$` | Engine version/build from group 1 |
| 1 | `mono` | Mono Runtime | Literal `Begin MonoManager ReloadAssembly` | — |
| 2 | `bootstrap` | Bootstrap | Literal `Bootstrap Startup` | — |
| 3 | `scene` | Scene Loading | Literal `Loading Scene` | — |
| 4 | `oxide` | Oxide Core | Literal `Loading Oxide Core` | Oxide version |
| 5 | `steamInitialized` | Steam Server Initialized | Literal `SteamServer Initialized` | — |
| 6 | `serverStartup` | Server Startup Complete | Literal `Server startup complete` | — |
| 7 | `bootstrapCompleted` | Bootstrap Completed | `^\[Bootstrap\] completed in (?<seconds>[0-9]+(?:\.[0-9]+)?)s$` | Bootstrap duration from `seconds` |
| 8 | `steamConnected` | Steam Server Connected | Literal `SteamServer Connected` | — |

The first implementation records only states. The capture groups and Oxide
version are deliberately not part of either API response, so adding their
storage or presentation later requires an explicit contract decision.

## Ordered milestone semantics

Each entry has one of these lower-case API values:

| State | Meaning |
| --- | --- |
| `pending` | No observed later milestone establishes whether this entry will occur in the active run. |
| `reached` | The matching log line was observed. |
| `skipped` | A later ordered milestone was observed while this entry had not been observed. |

The analyser is forward-only. When it observes position *N*, it marks every
earlier unresolved position as `skipped`, marks position *N* as `reached`, and
leaves later positions `pending`. Repeated matches are idempotent. A line for
an already skipped earlier position is ignored: once a later milestone has
established the gap, the state is not revised by an out-of-order line.

There is no timeout-based transition. For example, seeing `SteamServer
Initialized` after `Loading Scene`, without an Oxide line, produces
`reached, reached, reached, reached, skipped, reached, pending, pending,
pending`. This makes an absent Oxide installation a legitimate gap, not a
stalled `pending` state.

The final `steamConnected` position is never inferred as `skipped`; it stays
`pending` until its own line is observed. The authoritative completion rule is
therefore:

```text
steamConnected == reached  =>  started == true
```

All earlier entries may be `reached` or `skipped` when `started` is true. The
client must consume `started`, rather than reimplementing this rule.

## Proposed REST contract

The following contracts are proposed for the future implementation. JSON uses
the existing ASP.NET camel-case convention.

### `GET /api/server/startup/schema`

This is a stable presentation schema, normally fetched once when the UI is
constructed. It does not contact Docker and returns `200 OK` with
`application/json`.

```json
{
  "version": 1,
  "milestones": [
    { "id": "engine", "label": "Unity Engine" },
    { "id": "mono", "label": "Mono Runtime" },
    { "id": "bootstrap", "label": "Bootstrap" },
    { "id": "scene", "label": "Scene Loading" },
    { "id": "oxide", "label": "Oxide Core" },
    { "id": "steamInitialized", "label": "Steam Server Initialized" },
    { "id": "serverStartup", "label": "Server Startup Complete" },
    { "id": "bootstrapCompleted", "label": "Bootstrap Completed" },
    { "id": "steamConnected", "label": "Steam Server Connected" }
  ]
}
```

`version` is the required integer literal `1`. `milestones` is a required,
non-null array of exactly nine objects. Each object's `id` and `label` is a
required non-empty string. IDs, labels, order, and version are the schema
compatibility surface; log expressions and styling information are excluded.

### `GET /api/server/startup`

This polling endpoint returns a compact snapshot. It returns `200 OK` when
Docker can be queried, including when there is no currently running server.

```json
{
  "sessionId": "stp_Xk9dSY0EbiP1eRlzOEokEV-wvVYp8zdT6nF1nmWwBgs",
  "started": false,
  "milestones": [
    "reached",
    "reached",
    "reached",
    "reached",
    "skipped",
    "reached",
    "pending",
    "pending",
    "pending"
  ]
}
```

The fields are all required, except that `sessionId` is nullable:

| Field | JSON type | Meaning |
| --- | --- | --- |
| `sessionId` | string or `null` | Opaque identifier of the current concrete Rust run; `null` when there is no running Rust container. |
| `started` | boolean | Server-side result of the final-milestone completion rule. |
| `milestones` | array of nine strings | Positional states corresponding exactly to `schema.milestones`. Values are only `pending`, `reached`, and `skipped`. |

Both success responses use `application/json`. If
`RustServerControl:RustServerComposePath` is invalid or absent, the endpoint
returns the existing sanitized `500 ProblemDetails` pattern with
`application/problem+json`. If Docker cannot be launched or its metadata/log
command fails, it returns sanitized `502 ProblemDetails` with
`application/problem+json`. It must not return output from a failed Docker
metadata/log command as a successful partial snapshot.
No `409` is needed: reads do not mutate the server and may run while a lifecycle
or update operation is active.

## Run and `sessionId` semantics

A startup session is one concrete container run, identified internally by the
full Docker container ID and its `State.StartedAt` instant. Including the start
instant handles a Docker container that is restarted without being recreated.

The API never exposes either value. Instead, it emits a deterministic opaque
identifier such as `stp_` followed by base64url SHA-256 of a canonical
versioned tuple:

```text
rust-startup-v1 \0 full-container-id \0 started-at-in-UTC-round-trip-format
```

This is an identifier, not an authorization secret. It stays the same for the
same run even if Control restarts, changes for a recreated or restarted
container, and is unrelated to any update-operation GUID. No prior session is
returned after a new run is detected.

## Required scenario behaviour

| Situation | `GET /api/server/startup` result |
| --- | --- |
| Rust server stopped or service absent | `sessionId` is `null`, `started` is `false`, and all nine positions are `pending`. These placeholders do not describe an active startup attempt; callers use `sessionId == null` as the stopped/no-run signal. |
| New container is running, but no baseline log has appeared | A new non-null `sessionId`, `started: false`, and nine `pending` positions. |
| Intermediate startup | The one current session ID and states reconstructed from observed ordered lines; later entries remain `pending`. |
| Oxide is absent | On a later observed milestone, `oxide` becomes `skipped`; it is never left pending merely because no timeout elapsed. |
| Startup completed | `steamConnected` is `reached`, all preceding states are `reached` or `skipped`, and `started` is `true`. |
| Restart or another Rust run | The changed container-ID/start-time tuple creates a new session. Cached parser state for the old tuple is discarded; the snapshot begins again from the new run's available output. |
| Control restarts while Rust is already running | All in-memory parser state is lost. On the first request, Control identifies the current Docker run and rebuilds its state by replaying that container's retained output since its `StartedAt`. The deterministic session ID is the same as before the Control restart. No Control-owned persistent session/history is introduced. |

## Docker log source and collection strategy

The current `DockerComposeServerControl` calls the Docker CLI through
`IProcessExecutor` and knows the Compose path and service name `rust-server`.
Startup Tracking should use the same process-execution boundary, but in a
separate `DockerComposeStartupLogSource`; it must not put log parsing in
`DockerComposeServerControl`.

For each snapshot, the source should:

1. Run `docker compose -f <RustServerComposePath> ps -q rust-server` to find a
   running service container, following the existing lifecycle convention.
2. If an ID is returned, run `docker inspect` for its full ID and
   `State.StartedAt`.
3. Read that exact container with `docker logs --timestamps --since
   <started-at> <full-container-id>`, not `docker compose logs`. Direct
   container logs prevent an old/recreated Compose container from contaminating
   the session.
4. Normalize Docker timestamp prefixes, feed the resulting lines to the
   ordered analyser, and derive `started` only from the final state.

The implementation should verify the active run again when a container
disappears or changes during metadata/log collection, retry the complete read
once for the newly observed run, then return a sanitized gateway failure rather
than mix two runs in one response.

### On-demand analysis

The initial implementation replays the current run's retained logs from the
container start on each request; it does not use a continuously running log
follower or retain Control-owned state. This gives every poll a complete,
coherent snapshot and makes Control-restart reconstruction identical to a
normal first request. A future in-memory per-run cache may read from the last
observed Docker timestamp instead; replaying the boundary is safe because
matching is idempotent.

This is the smallest reliable fit for the current repository: `IProcessExecutor`
captures finite command output, no streaming process abstraction or startup
hosted service exists, and the UI already polls. It avoids a long-lived Docker
child process, reconnect logic, extra concurrent state, and a persistent store,
while Docker output permits replay after a Control restart.

The initial replay requires the Docker logging driver to retain the current
container's startup output. If `docker logs` fails, the endpoint returns `502`.
If deployment log rotation has already discarded early lines, Docker CLI output
cannot distinguish that case from a run that never emitted them; the snapshot
can only report the lines still observed and must not infer completion from
WebRCON. This retention requirement is an implementation review gate (see
below), not a reason to use a timeout or alter milestone semantics.

## Architecture and expected future files

The feature is a fourth capability alongside lifecycle, readiness, and
operations. Its dependency flow should be:

```text
API endpoint -> Application startup-tracking interface -> Infrastructure service
                                                    -> Docker startup log source
                                                    -> IProcessExecutor
                                      Domain ordered analyser and snapshot types
```

Suggested files for the implementation are:

| Project | Suggested files and responsibility |
| --- | --- |
| `RustServer.Control.Domain` | `StartupTracking/StartupMilestoneState.cs`, `StartupMilestoneDefinition.cs`, `StartupTrackingSchema.cs`, `StartupSnapshot.cs`, and `StartupMilestoneAnalyzer.cs` for the fixed version-1 schema, state model, matching, and forward-only transitions. |
| `RustServer.Control.Application` | `StartupTracking/IStartupTrackingService.cs`, `IStartupLogSource.cs`, and run/log batch contracts. These define use cases without Docker CLI details. |
| `RustServer.Control.Infrastructure` | `StartupTracking/StartupTrackingService.cs` for the cache and snapshot orchestration; `DockerComposeStartupLogSource.cs` for Compose/container metadata and logs; source-specific exception types as needed. Extract the currently private Compose service-name/request construction into a small shared Docker-Compose descriptor so this source and `DockerComposeServerControl` cannot drift. |
| `RustServer.Control.Api` | DI registrations, the two minimal API mappings, response records with XML documentation, and the same configuration/process exception-to-ProblemDetails handling convention as existing status operations. |
| `RustServer.Control.Tests` | Domain, Docker-source, application-service, and API contract tests listed below. |

`DockerComposeServerControl` remains responsible only for lifecycle and its
existing WebRCON readiness check. It neither owns startup state nor parses
logs. The startup source should share Docker invocation configuration, not
server-control behaviour or RCON code.

## Expected future tests

- Schema tests assert version `1`, all nine IDs, labels, and ordering.
- Analyzer tests cover every literal and regular-expression matcher, repeats,
  all-reached flow, a missing Oxide milestone, multiple skipped gaps, no
  timeout transition, and a late earlier line after a skip.
- Completion tests prove that `started` is false until an actual
  `SteamServer Connected` line and true only after that line; the final entry
  is never inferred as skipped.
- Docker-source tests assert Compose service lookup, inspect of ID and start
  time, exact-container log command, timestamp normalization, command errors,
  cancellation, and a container-change race.
- Service tests cover stopped placeholders, new-run cache reset, incremental
  replay/idempotent boundary reads, deterministic session-ID construction, and
  reconstruction by a fresh service instance representing a Control restart.
- API tests assert the exact JSON names, nullable stopped `sessionId`,
  positional nine-state array, schema stability, `500`/`502` failures, and
  that `GET /api/server/status` still has only `isRunning` and `isReady`.
- Once implementation begins, OpenAPI tests and generated `openapi.json` /
  `docs/api-contract.md` must be updated together using the repository's
  contract generator.

## Explicitly out of scope

- Modifying lifecycle, readiness, RCON, server command, update, or operation
  endpoint semantics.
- Reusing update operation IDs.
- Database, Redis, event sourcing, persistent startup history, or recovery of
  Control-owned state across a Control restart.
- SignalR, SSE, Kafka, a log microservice, a live console, or a public log
  streaming/viewing endpoint.
- Parsing failures, warnings, plugin status, arbitrary log content, or
  diagnostics beyond the nine baseline milestones.
- Exposing matcher expressions, Docker container IDs, Docker timestamps, or
  optional extracted values in version 1.
- Inferring skipped entries from elapsed time or inferring completion from
  `isReady`.

## Review question before implementation

The deployment needs to guarantee that the active container's Docker logging
driver retains output from `State.StartedAt` for at least the period in which a
Control restart may need to reconstruct startup progress. Without that
guarantee, Docker can return a syntactically successful but truncated history,
and the API cannot truthfully distinguish it from an incomplete startup using
the proposed compact three-state contract. Confirm the logging-driver retention
policy (or explicitly accept this limitation) before implementation.
