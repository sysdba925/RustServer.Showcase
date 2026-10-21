# Iteration 021 — Overview Rust and Oxide update workflow

## Goal

Connect the first Overview screen to the published asynchronous Rust and Oxide
update operations, from an operator request through authoritative observation
and reconciliation, without inventing operation history or compound actions.

## Added and updated

- Rust and Oxide cards now expose disabled or enabled update controls based on
  the existing Overview action policy and show a stopped-server explanation
  when an otherwise available update cannot run.
- Control update requests retain their accepted operation ID, accepted status,
  and derived requested target separately from authoritative operation detail.
- Overview immediately looks up an accepted operation and sequentially polls
  its detail every two seconds while it is Pending, Running, or an unknown
  source status. Active updates block lifecycle and update mutations and
  activate the existing Maintenance presentation for known update types.
- Succeeded and Failed operation results remain visible. Terminal or
  unavailable operation detail triggers one best-effort, independently owned
  refresh of Server, Rust Versions, and Oxide Versions.
- Update-request conflicts remain local to the relevant software card and
  trigger the same observable-resource reconciliation. A 404 lookup is
  displayed as unavailable detail, not as deleted durable history.
- Overview uses one mutation gate for Start, Stop, Restart, Rust update, and
  Oxide update. AbortController ownership and latest-result guards prevent
  abandoned polls or refreshes from replacing newer state.
- Shared Control and Versions API types now model optional nullable fields
  exactly; Control also exposes Rust update, Oxide update, and known-operation
  lookup functions.

## Current capability

With the server authoritatively Stopped and a reported software update
available, an operator can request either update. The current page session
shows accepted context while detail loads, then the source operation state,
and finally refreshed Server and version resources after a terminal result or
unavailable detail. A fresh page still cannot discover an operation started by
another client or an earlier page session.

## Deliberately deferred

- Operation list or durable history, operation cancellation, retry controls,
  live output, progress percentages, and progress stages.
- Automatic Stop → Update → Start workflows and automatic restart after an
  Oxide update.
- Permanent software or operation polling, global state, React Query, and
  SWR.

## Validation

- `npm run check` passed: lint, TypeScript typecheck, 99 Vitest tests, and
  production Vite build completed successfully.
- `git diff --check` passed.
- RustServer.Control API-contract verification passed without writing.
- RustServer.Versions API-contract verification could not run because its
  existing API process locked the generated executable during build; no backend
  source or contract was changed.

## Decisions

- Accepted responses are partial source context, not fabricated
  `OperationResponse` values; operation type, timestamps, exit code, and error
  remain absent until the lookup returns them.
- Unknown source statuses remain explicit, block mutations, and continue the
  sequential poll rather than being inferred as terminal.
- Operation data exists only while this page knows an accepted ID; no frontend
  reconstruction of current operations or durable history is attempted.
