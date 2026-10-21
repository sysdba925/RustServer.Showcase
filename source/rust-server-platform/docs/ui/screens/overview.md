# Overview screen

| Property | Contract |
| --- | --- |
| Route | `/` |
| Status | Current |
| Purpose | Read-oriented server summary, software state, and known update activity. |

Overview follows the order Server, Software, then Activity. Its Server region
shows the derived server state, a concise description, and a `Manage server`
shortcut to `/server`. Lifecycle controls and detailed Running/Ready values are
deliberately absent; they belong to the [Server screen](server.md).

Regions render independently. A failed Control request must not hide a
successful Rust or Oxide card, and a failed Rust request must not hide a
successful Oxide card. See [state semantics](../states.md) and
[API mapping](../api-mapping.md).

## Server summary

| Concern | Contract |
| --- | --- |
| Data source | `GET /api/server/status`; startup schema and snapshot for compact presentation while the server is Starting. |
| SOURCE fields | `isRunning`, `isReady`; startup schema labels/order and snapshot `sessionId`, `started`, positional milestone states. |
| DERIVED fields | Stopped, Starting, Ready, local Inconsistent / Unavailable presentation; resolved startup step count. |
| Actions | `Manage server` navigation only. |

When Starting and a startup session is available, the summary may show
`Startup: resolved / total milestones`. A milestone is resolved when its source
state is `reached` or `skipped`; this is a step count, never a percentage or
time estimate. If tracking is unavailable, Overview says so compactly without
inventing a startup failure. Ready and Stopped summaries do not retain the
completed milestone list.

## Software and Activity

Rust and Oxide cards retain their existing independently loaded version data
and update actions. Update eligibility still requires known Stopped state and
no active operation. Activity continues to show only a known accepted update
operation; there is no history or operation-list API.
