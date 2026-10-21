# Frontend state semantics

This document defines UI state independently of styling. It uses API fields and
operation contracts rather than an invented server `state` property.

## State layers

### Request state

Each independently requested resource has one of these request states:

| State | Meaning | UI consequence |
| --- | --- | --- |
| Loading | A request has started and no usable response is available for that resource. | Show local loading; do not replace unrelated successful regions. |
| Available | The resource has a current usable response. | Render the region from that response. |
| Unavailable | The request failed, timed out, returned unusable data, or its service cannot be reached. | Render local error and retry; disable only actions that depend on it. |

`RustServer.Control`, Rust version data, and Oxide version data are independent
resources. Control can be unavailable while either Versions response remains
usable. One failed request must not blank the whole application.

### Derived server runtime state

`GET /api/server/status` contains `isRunning` and `isReady`; it does **not**
contain a `state` field. The UI derives:

| UI state | Rule | Meaning |
| --- | --- | --- |
| Stopped | `isRunning == false && isReady == false` | Rust process/container is not running. |
| Starting | `isRunning == true && isReady == false` | Process/container runs, but the server is not ready. |
| Ready | `isRunning == true && isReady == true` | Server is running and accepting the readiness check. |
| Inconsistent / Unavailable | `isRunning == false && isReady == true` | Defensive handling for an invalid status combination. |

When Control status is unavailable, server state is `Unavailable`, not a guess
from stale version or RCON data. The `false / true` combination is
`Inconsistent / Unavailable`: do not reinterpret it as Ready or Stopped,
show a local status error, disable lifecycle and update mutations, and allow
refresh/retry. It is defensive frontend handling, not a backend state or API
field; the standard status implementation does not normally produce it.

### Update operation state and maintenance

Rust and Oxide updates are asynchronous Control operations:

```text
Pending -> Running -> Succeeded
                   -> Failed
```

`Pending` and `Running` are active. `Succeeded` and `Failed` are terminal.
Operation type is `RustUpdate` or `OxideUpdate`.

`Maintenance` / `Operation active` is frontend-derived presentation while a
known Rust or Oxide update is Pending or Running. It is not a server-status
value and does not erase the underlying Stopped, Starting, or Ready status.

The UI can observe an operation only after receiving its ID from an accepted
update (or otherwise already knowing the ID). There is no endpoint to discover
or list operations. The Control operation store is in memory, so a service
restart discards records; the UI must not call this durable history.

## Action availability

“Eligible update” means the corresponding Versions response is available,
reports `updateAvailable`, the server status is available and Stopped, and no
operation is active.

| UI state | Start | Stop | Restart | Rust Update | Oxide Update |
| --- | --- | --- | --- | --- | --- |
| Stopped | Available | Unavailable; no-op | Unavailable; API would start a stopped server, surprising for Restart | Available when eligible | Available when eligible |
| Starting | Unavailable; already running | Available | Unavailable; first-implementation UX policy | Unavailable; server is running | Unavailable; server is running |
| Ready | Unavailable; already running | Available | Available | Unavailable; server is running | Unavailable; server is running |
| Maintenance / active update | Unavailable | Unavailable | Unavailable | Unavailable | Unavailable |
| Unavailable | Unavailable | Unavailable | Unavailable | Unavailable | Unavailable |

Starting means the container/process is already running but Rust is not Ready.
The first implementation exposes Stop so an operator can abort a slow or
unhealthy startup; the backend supports that action. Restart is deliberately
unavailable while Starting even though the backend technically supports it.
The operator can restart after reaching a stable state if needed. This is a
frontend UX policy, not a backend limitation. Backend conflicts remain
authoritative: all lifecycle mutations are rejected while an update is active.

A successful lifecycle request returns status, which is authoritative for the
immediate presentation; normal status refresh then reconciles it. Lifecycle
requests are synchronous, unlike updates.

## Mutation and conflict rules

- Rust update requires the server to be stopped.
- Oxide update requires the server to be stopped.
- Only one server-mutating update can be Pending or Running at one time.
- Lifecycle mutations are blocked while an update operation is active.
- Updates never imply a compound “Stop → Update → Start” workflow; the UI
  issues only the explicit action selected by the operator.
- Disabled actions assist the operator but do not remove races with another
  client or changed state. A backend `409 Conflict` is possible and must show
  the returned problem, then refresh relevant status and known operation data.

There is no operation cancellation, live output, progress percentage, or stage
API. Polling and retry mechanics are implementation concerns to define with the
frontend API layer; this product contract requires only that an accepted
operation can be observed until it is terminal or becomes unavailable.

## Plugins state model

Plugins state is independent of Rust lifecycle state. In particular, logical
Plugins Environment is not a server Running, Ready, Stopped, or Starting state,
and deployed Source is not an Environment selector value.

| Dimension | States | Rule |
| --- | --- | --- |
| Environment | Staging; Production | `GET /api/environment` establishes the current logical Plugins Environment when its request is Available. An Unavailable request does not authorize an assumed Environment. |
| Deployed Source | Development; Release; Unknown / unavailable | This is the deployed baseline source, not a local selection draft. It is Unknown / unavailable whenever the current contract responses cannot establish a valid baseline or source. |
| Runtime changes | None; Present; Unavailable | A successful applicable runtime comparison with an empty list means None; a non-empty list means Present. Missing, failed, inapplicable, or unusable comparison data is Unavailable, never None. |
| Request | Loading; Available; Unavailable | Each Environment, deployment, release, package, and runtime request is independent. Loading has no usable response; Available has a current usable response; Unavailable failed, timed out, or produced unusable data. |

The only established Environment and Source combinations are:

| Environment | Deployed Source |
| --- | --- |
| Staging | Development |
| Staging | Release |
| Production | Release |

`Production + Development` is forbidden by the backend safety invariant, not
merely hidden by the UI. Unknown / unavailable Source is not an additional
valid deployment combination; it is a fail-closed representation when the
contract cannot establish state.

### Environment and Release transitions

```text
Staging + Development
→ PUT /api/environment { Production }
→ 409 Conflict
→ operator explicitly deploys a Release first
```

Web must report this conflict and preserve operator choice. It must not
automatically deploy a Release as a substitute for the operator's action.

Changing Environment is not a deployment or Rust lifecycle operation:

- `Staging + Release N → Production` retains the Release N baseline; it does
  not redeploy it.
- `Production + Release N → Staging` retains the Release N baseline; it does
  not redeploy it.
- An Environment switch neither selects a Release nor starts, stops, or
  restarts Rust.

### Production visual safety

Production requires persistent visual context throughout operator-facing
Plugins surfaces. The operator must continue to be able to tell that Plugins
is in Production, rather than seeing that fact only at the moment of a switch.
This requirement deliberately does not choose a color, banner, header design,
or icon.

## Software-state implications

Rust `installedBuildId` and `availableBuildId` are comparable Steam Build IDs.
Its `running` field is different RCON-derived metadata, not a Build ID; a Rust
restart-required condition cannot be derived reliably from current contracts.

Oxide `installedVersion`, `runningVersion`, and `availableVersion` are
independent. When stopped, `runningVersion` is `null`. The UI may derive
`Oxide restart required` when
`runningVersion != null && installedVersion != runningVersion`.
