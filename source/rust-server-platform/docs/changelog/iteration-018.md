# Iteration 018 — RustServer.Web Overview lifecycle controls

## Goal

Connect the published Control lifecycle actions to the Overview Server panel
while retaining existing resource loading and leaving update workflows out of
scope.

## Added and updated

- Overview now derives lifecycle availability from the current `OverviewInput`
  through `deriveActionAvailability` and passes the result to the
  presentational `ServerPanel`.
- The Server panel exposes native Start, Stop, and Restart buttons with the
  documented stopped, starting, ready, inconsistent, and active-operation
  availability policy.
- `OverviewPage` owns the pending lifecycle action, a mutation-local error,
  and a local lock so only one lifecycle request can run at a time. Pending
  text names the requested action and disables every lifecycle control.
- Successful lifecycle responses immediately replace the Server resource with
  the returned `ServerStatus`; lifecycle `HttpError` ProblemDetails display
  `detail`, then `title`, while retaining the known Server state.
- Focused ServerPanel and OverviewPage tests cover action availability,
  callbacks, pending controls, successful Start/Stop/Restart responses,
  lifecycle conflict presentation, and duplicate-request prevention.

## Current capability

An operator can request Start from Stopped, Stop from Starting or Ready, and
Restart from Ready. The Server region immediately reflects the authoritative
synchronous lifecycle response. A rejected lifecycle request remains local to
that region rather than making the separately loaded status unavailable.

## Deliberately deferred

- Rust and Oxide update mutations, operation lookup and polling, readiness
  reconciliation, manual refresh, and periodic server polling.
- Lifecycle cancellation, progress, stages, history, React Context, global
  state, React Query, and SWR.

## Validation

- `git diff --check` passed.
- `npm run check` was attempted but could not run because neither Node.js nor
  npm is installed or available on PATH in the execution environment. Lint,
  typecheck, tests, and production build remain to be run in a Node-enabled
  environment.

## Decisions

- `OverviewPage` owns lifecycle mutation state and invokes the existing shared
  API functions; ServerPanel stays presentational and contains no transport
  imports.
- Lifecycle eligibility remains a model policy; JSX only combines its derived
  result with the local pending-mutation gate.
- A synchronous lifecycle response is sufficient for the immediate state; no
  additional status request or automatic polling is introduced.
