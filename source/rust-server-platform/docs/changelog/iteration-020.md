# Iteration 020 — Overview software lifecycle reconciliation

## Goal

Keep Rust and Oxide runtime metadata aligned with server lifecycle transitions
without making version data part of the temporary readiness-polling loop.

## Added and updated

- Overview refreshes the independent Rust and Oxide version resources after a
  successful Start, Stop, or Restart response while immediately retaining that
  response as the Server state.
- When temporary readiness reconciliation moves from Starting to Ready or
  Stopped, Overview performs one further software refresh. Repeated Starting
  responses and Inconsistent responses do not refresh software.
- Software refreshes cancel the previous refresh and accept results only from
  the current refresh, preventing late runtime metadata from replacing newer
  values.
- Existing successful Rust and Oxide values remain visible while a replacement
  request is in flight; failures remain local to their respective resource.
- Focused Overview coverage now includes lifecycle reconciliation, independent
  failures, retained data, and latest-refresh-wins cancellation.

## Current capability

Operators see `running: null` and `runningVersion: null` after a successful
stop reconciliation, and current running metadata after readiness reaches a
stable Ready state. Rust and Oxide remain independently available or
unavailable throughout those refreshes.

## Deliberately deferred

- Rust or Oxide update mutations, operation lookup or polling, progress,
  cancellation, and history.
- Permanent/background software polling and a manual refresh control.
- React Query, SWR, and global state for Overview resources.

## Validation

- `npm run check` passed: lint, TypeScript typecheck, 74 Vitest tests, and the
  production Vite build completed successfully.

## Decisions

- OverviewPage owns lifecycle-aware software orchestration; shared API clients
  remain single-request transports.
- The two-second readiness loop requests only Control server status. Software
  is refreshed only after a successful lifecycle mutation and the explicit
  Starting-to-Ready/Stopped transition.
