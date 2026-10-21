# Server screen

| Property | Contract |
| --- | --- |
| Route | `/server` |
| Status | Current |
| Purpose | Operate and inspect Rust Dedicated runtime lifecycle and startup progress. |

## Runtime

The Runtime region presents the derived Stopped, Starting, Ready, or local
Inconsistent / Unavailable state from `GET /api/server/status`. It includes the
source Running and Ready values, lifecycle mutation feedback, and Start, Stop,
and Restart subject to the action matrix in [state semantics](../states.md).
Known active update operations remain a derived Maintenance overlay and block
lifecycle actions.

## Startup

The Startup region loads the fixed Control startup schema and the current
snapshot. It presents backend-provided labels in schema order with the
authoritative positional `pending`, `reached`, and `skipped` states. Its list
is a constrained, left-aligned ordered list so each label remains associated
with its state badge on wide and narrow screens.

The snapshot is observed only while it has a non-null `sessionId` and source
`started` is false. A new session replaces the old snapshot wholesale;
milestones are never merged, reconstructed from logs, or inferred from
readiness. Readiness reconciliation is independent of startup polling.
