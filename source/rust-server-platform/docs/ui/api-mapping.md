# UI-to-API mapping

This is the product UI data contract. It distinguishes existing API data,
frontend interpretation, and future product ideas.

| Category | Meaning |
| --- | --- |
| **SOURCE** | Directly returned by an existing API. |
| **DERIVED** | Computed by the frontend from existing API fields or a known request result. |
| **FUTURE** | Represented conceptually/design-wise but not supplied by the backend. |

API base URLs are future application configuration. They must not be hardcoded
to the current development VM.

## RustServer.Control

| Endpoint | UI use | Current response / outcome |
| --- | --- | --- |
| `GET /api/server/status` | Read server runtime. | `isRunning`, `isReady` on success. |
| `GET /api/server/configuration` | Read desired and effective game-server configuration. | Typed desired configuration and nullable effective configuration; they can differ until a successful start. |
| `PUT /api/server/configuration` | Change desired game-server configuration. | Returns the updated desired value; does not restart Rust. |
| `POST /api/server/start` | Start lifecycle action. | Returns `isRunning`, `isReady`; conflicts with active update. |
| `POST /api/server/stop` | Stop lifecycle action. | Returns `isRunning`, `isReady`; conflicts with active update. |
| `POST /api/server/restart` | Restart lifecycle action. | Returns `isRunning`, `isReady`; conflicts with active update. A stopped server is started by this endpoint. |
| `POST /api/server/wipe` | Request an explicit stopped-server wipe. | `202 Accepted` with operation lookup; plan independently selects `keep` or `delete` for world and blueprints. It does not start Rust or create a snapshot. |
| `GET /api/server/startup/schema` | Read ordered startup presentation schema. | Source `version`, ordered milestone `id`, and label. |
| `GET /api/server/startup` | Observe startup progress for the current Rust run. | Source nullable `sessionId`, `started`, and positional `pending`, `reached`, or `skipped` milestone states. |
| `POST /api/server/update` | Request Rust update. | `202 Accepted` with operation `id`, `status`, and lookup location; `409` if server runs or an update is active. |
| `POST /api/updates/oxide` | Request Oxide update. | Same accepted-operation pattern and conflicts as Rust update. |
| `GET /api/operations/{id}` | Observe one known update or wipe. | ID, type, status, timestamps, exit code, error, and a path-free `wipeResult` after a wipe; `404` if not found. |

`POST /api/server/command` exists only in Development as an arbitrary RCON
debug endpoint. It is not a production frontend feature and must not define a
Console area.

### Server and operation concepts

| UI concept | Category | Mapping / rule |
| --- | --- | --- |
| Server running | SOURCE | `status.isRunning`. |
| Server ready | SOURCE | `status.isReady`. |
| Server starting | DERIVED | `isRunning && !isReady`. |
| Stopped | DERIVED | `!isRunning && !isReady`. |
| Ready | DERIVED | `isRunning && isReady`. |
| Inconsistent / Unavailable | DERIVED | `!isRunning && isReady`; show local status error, disable mutations, and allow refresh/retry. |
| Startup milestone labels and order | SOURCE | `GET /api/server/startup/schema`; belongs to the Server screen. |
| Startup milestone state | SOURCE | Positional `GET /api/server/startup` states; snapshot replaces an earlier session wholesale. |
| Overview startup count | DERIVED | While Starting with a source session, count states other than `pending`; show a count rather than time or percentage. |
| Maintenance / active operation | DERIVED | A known `RustUpdate`, `OxideUpdate`, or `Wipe` with `Pending` or `Running` status. |
| Operation ID | SOURCE | `id` in accepted response and lookup. |
| Operation type | SOURCE | `type` in lookup: `RustUpdate`, `OxideUpdate`, or `Wipe`. |
| Operation status | SOURCE | `status`: `Pending`, `Running`, `Succeeded`, `Failed`. |
| Operation timestamps | SOURCE | `createdAt`, `startedAt`, `finishedAt` from lookup. Nullable values stay absent. |
| Operation exit code / error | SOURCE | `exitCode` and `error` from lookup. |
| Operation duration | DERIVED | Only when required source timestamps exist. |
| Persistent activity history | FUTURE | No list/history endpoint; operations live only in Control-process memory. |

An accepted update gives ID and status, but not type or timestamps. The UI knows
the requested type for immediate local context, then treats operation lookup as
the source of record.

## RustServer.Versions — Rust

`GET /api/versions/rust` returns `branch`, `installedBuildId`, `running`,
`availableBuildId`, and `updateAvailable`.

| UI concept | Category | Mapping / rule |
| --- | --- | --- |
| Rust branch | SOURCE | `branch`. |
| Rust installed | SOURCE | `installedBuildId`, Steam Build ID from installed server manifest. |
| Rust running metadata | SOURCE | `running`: protocol, build date, Unity version, changeset, branch; null when stopped. |
| Rust available | SOURCE | `availableBuildId`, Steam Build ID for configured branch. |
| Rust update available | SOURCE | `updateAvailable`, comparison of installed and available Build IDs. |
| Rust build freshness | DERIVED | `installedBuildId == availableBuildId`; must agree with `updateAvailable`. |
| Rust restart required | FUTURE | `running` has no Steam Build ID, so comparison with installed cannot be reliable. |

Installed and available Rust values are comparable Build IDs. `running` is
RCON metadata, not a hidden third Build ID; it must not be guessed or converted
into one.

## RustServer.Versions — Oxide

`GET /api/versions/oxide` returns `installedVersion`, `runningVersion`,
`availableVersion`, and `updateAvailable`.

| UI concept | Category | Mapping / rule |
| --- | --- | --- |
| Oxide installed | SOURCE | `installedVersion` from installed assembly. |
| Oxide running | SOURCE | `runningVersion` through RCON; null when server is stopped. |
| Oxide available | SOURCE | `availableVersion`, current available release. |
| Oxide update available | SOURCE | `updateAvailable`, installed versus available comparison. |
| Oxide restart required | DERIVED | `runningVersion != null && installedVersion != runningVersion`. |
| Oxide installed latest | DERIVED | `installedVersion == availableVersion` when data is available. |

Installed, running, and available Oxide values are independent. A mismatch may
be observed whenever the API reports different installed and running versions;
the current contracts do not establish how it arose.

## RustServer.Plugins

Plugins APIs support the seven documented operator workflows. This mapping
groups published operations by operator purpose; it is not a navigation model
or a one-endpoint-per-screen design.

| Operator capability | Published operations | Operator purpose |
| --- | --- | --- |
| Read or change logical Environment | `GET /api/environment`<br>`PUT /api/environment` | Read the persistent logical Plugins Environment and reconfigure the current instance between Staging and Production. A switch does not deploy, start, stop, or restart Rust, and does not select a Release. A Development baseline cannot switch to Production (`409 Conflict`); the operator must explicitly deploy a Release first. |
| Inspect and apply an update package | `POST /api/update-packages`<br>`GET /api/update-packages/{id}`<br>`GET /api/update-packages/{id}/changes`<br>`GET /api/update-packages/{id}/diff`<br>`POST /api/update-packages/{id}/commit` | Upload one package, inspect its classified incoming changes and bounded text diffs, then commit only selected Managed paths to Development. One package remains one operator operation even when it produces commits in both managed repositories; committing does not deploy. |
| Deploy current Development to Staging | `POST /api/deployments/development` | Explicitly materialize the current clean Development source on the Staging target. It is a deployment, not a source-selector change, and can conflict when Managed runtime changes require an explicit Import or Discard first. |
| Create and inspect immutable Releases | `POST /api/releases`<br>`GET /api/releases`<br>`GET /api/releases/{id}` | Create, list, and inspect immutable Release metadata tied to exact Git commits. Creating a Release does not deploy. |
| Deploy a selected Release to Staging | `POST /api/staging/releases/{releaseId}/deploy` | Explicitly materialize the selected immutable Release on the current Staging target, including a switch from a Development baseline or a rollback to an older Release. |
| Review Staging runtime changes | `GET /api/staging/runtime-changes`<br>`GET /api/staging/runtime-changes/diff` | Compare Managed target files to the exact deployed baseline and inspect an available bounded text diff. Incoming package changes and runtime changes remain separate workflows. |
| Resolve selected Staging runtime changes | `POST /api/staging/runtime-changes/import`<br>`POST /api/staging/runtime-changes/discard` | Import selected Development-baseline runtime changes into Development, or explicitly discard selected changes to either allowed Staging baseline. Neither action redeploys or changes the deployment baseline. |
| View or deploy Production Release baseline | `GET /api/production/deployment`<br>`POST /api/production/releases/{releaseId}/deploy` | View the Release deployed to Production and explicitly deploy a selected immutable Release, including an older Release as rollback. Production accepts Release source only. |

Environment and deployed Source are separate dimensions. The valid established
combinations are `Staging + Development`, `Staging + Release`, and
`Production + Release`; `Production + Development` is forbidden. A successful
Environment response alone does not establish the deployed Source. The current
OpenAPI has no generic Staging deployment-status operation, so UI state must
remain `Unknown / unavailable` when a contract response cannot establish a
baseline or source; it must not infer a source from an Environment change or a
local selection draft.

The immutable Release catalog is not a plugin version-discovery or catalog
scraping capability. Plugin list, version discovery, and catalog scraping
remain future until a published backend contract provides them.

## RustServer.PluginCatalog

The independent Catalog routes are `/dashboard/plugins/catalog` and
`/dashboard/plugins/catalog/:pluginId`. They use the same-origin
`/plugin-catalog-api` prefix and represent known metadata only, never the
installation, deployment, or runtime state owned by RustServer.Plugins.

| Operator capability | Published operations | Operator purpose |
| --- | --- | --- |
| Browse and maintain catalog metadata | `GET`, `POST /api/plugins`<br>`GET`, `PUT`, `DELETE /api/plugins/{pluginId}` | Manage known plugin identity, description, known version and timestamps. |
| Maintain catalog sources | `GET`, `POST`, `PUT`, `DELETE /api/plugins/{pluginId}/sources` | Record manually maintained external source metadata; no scraping or version check is inferred. |
| Maintain catalog media | `GET`, `POST /api/plugins/{pluginId}/media`<br>`POST /api/plugins/{pluginId}/media/external`<br>`PUT`, `DELETE /api/plugins/{pluginId}/media/{mediaId}`<br>`PUT /api/plugins/{pluginId}/icon` | Manage Catalog-owned local media and external Video/Preview links; local bytes stay backend-owned. |

Catalog records, sources, versions and media are not a Plugin installation,
release, deployment, runtime change, or Rust lifecycle capability.

## Future concepts

The following are **FUTURE** until a backend contract exists: persistent
activity history, plugin list, plugin version discovery/catalog scraping,
players, logs, metrics, notifications, server uptime/start time, server name,
and production console.

There is also no operation listing, cancellation, live output, percentage
progress, stage model, or World API for current-world data, snapshots, backup,
or restore. The existing wipe API is a backend capability; the current World
screen does not yet provide controls for it.
