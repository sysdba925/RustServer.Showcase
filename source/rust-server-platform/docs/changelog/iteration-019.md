# Iteration 019 — RustServer.Web Starting-state reconciliation

## Goal

Reconcile a frontend-derived Starting state with the authoritative Control
status until the server reaches a stable state, without introducing permanent
background polling.

## Added and updated

- Overview now starts a local readiness-reconciliation loop only while the
  available Server resource derives to Starting and no lifecycle mutation is
  pending.
- Reconciliation waits two seconds before each `GET /api/server/status` and
  schedules the next request only after the preceding request completes, so
  requests cannot overlap.
- Authoritative polling responses immediately update the Server resource.
  Polling stops on Ready, Stopped, Inconsistent, unavailable status, or a
  pending lifecycle mutation.
- Beginning a lifecycle mutation synchronously clears a scheduled readiness
  timer and aborts an in-flight polling request. Aborted or late responses
  cannot overwrite the lifecycle response.
- Initial Overview loads that already report Starting use the same temporary
  reconciliation behavior. `LifecycleAction` now belongs to the Overview
  model boundary rather than ServerPanel.
- Focused Overview tests cover sequential Start reconciliation, initial
  Starting, poll failure, Stop cancellation and abort behavior, and the lack
  of polling in Ready and Stopped states.

## Current capability

After Start or Restart returns Starting, the Server region remains immediately
authoritative and then refreshes status every two seconds until it becomes
Ready, Stopped, Inconsistent, or unavailable. Operators can still stop a
Starting server; the Stop response remains authoritative over any abandoned
polling request.

## Deliberately deferred

- Permanent/background polling, a manual refresh control, and reconciliation
  of Rust or Oxide version resources.
- Rust/Oxide update mutations, operation lookup or polling, progress,
  cancellation, history, React Context, global state, React Query, and SWR.

## Validation

- `git diff --check` passed.
- `npm run check` remains unavailable because Node.js and npm are not installed
  or available on PATH in the execution environment. Lint, typecheck, tests,
  and production build remain to be run in a Node-enabled environment.

## Decisions

- Readiness polling is frontend orchestration in OverviewPage; `getServerStatus`
  remains a single shared-API request.
- The polling loop uses timer cleanup, AbortController cancellation, and a
  mutation-pending guard rather than an isMounted flag, and remains safe under
  React StrictMode setup and cleanup cycles.
