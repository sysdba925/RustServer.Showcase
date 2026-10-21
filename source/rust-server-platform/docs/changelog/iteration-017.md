# Iteration 017 — RustServer.Web Lifecycle API boundary

## Goal

Add typed, bodyless Control lifecycle requests to the frontend API boundary
without connecting lifecycle behavior to React or adding any update capability.

## Added and updated

- `shared/api` now exposes typed `startServer`, `stopServer`, and
  `restartServer` functions for the published Control lifecycle endpoints.
- A small bodyless POST JSON helper reuses the GET layer's shared request and
  non-success handling. Lifecycle requests use the configured Control base URL
  and existing development proxy path.
- `HttpError` now preserves request method, URL, HTTP status, status text, and
  a valid contract-shaped `ProblemDetails` body when one is returned as
  `application/problem+json`.
- Focused Control client tests cover all three lifecycle URLs, POST method,
  absent request body, AbortSignal forwarding, successful `ServerStatus`,
  conflict ProblemDetails, and malformed problem-body fallback behavior.

## Current capability

Consumers of the public shared API can send one of the three published Rust
server lifecycle requests and receive `ServerStatus` on success. They can catch
`HttpError` to inspect a lifecycle conflict's contract-shaped ProblemDetails.
No React component invokes these functions yet.

## Deliberately deferred

- Lifecycle buttons, click handlers, action availability wiring, mutation
  reconciliation, retry behavior, and user-facing conflict presentation.
- Rust/Oxide update mutations, operation lookup or polling, cancellation,
  progress, history, and live output.
- React Context, global state, React Query, SWR, and periodic refresh.

## Validation

- `git diff --check` passed for the lifecycle API and changelog changes.
- `npm run check` could not run because Node.js and npm were unavailable in the
  execution environment; it remains required before integration.

## Decisions

- The shared API boundary represents transport results only. Lifecycle action
  eligibility remains a frontend policy owned by Overview model derivations.
- A contract-shaped `application/problem+json` response is preserved for later
  UI handling; malformed or absent problem bodies still produce a diagnostic
  HTTP error.
- Lifecycle API functions remain unconnected to React until the dedicated UI
  integration task.
