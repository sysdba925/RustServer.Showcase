# Iteration 024 — Overview startup tracking

## Goal

Make the published Rust startup-tracking capability visible on the Overview
screen without changing lifecycle, WebRCON readiness, or update-operation
semantics.

## Added and updated

- RustServer.Web now loads the fixed startup milestone schema once per
  Overview mount and renders its backend-provided labels and order.
- Overview independently loads and sequentially polls the current startup
  snapshot every two seconds while its source `sessionId` is present and its
  source `started` value is false.
- The Server panel presents pending, reached, and skipped milestones. Skipped
  is a neutral source state, not a failure.
- Empty stopped-server snapshots render as no active startup session. A newer
  snapshot replaces the previous session wholesale; progress is never merged
  across session IDs.
- Startup requests are abortable and guarded so abandoned or older responses
  cannot replace a newer snapshot.

## Current capability

Operators can see the current Rust run's ordered startup milestones while it is
being observed, and can distinguish backend-confirmed startup completion from
independent WebRCON readiness.

## Deliberately deferred

- Startup history, raw container details, log viewing or streaming, timestamps,
  percentage progress, health scoring, and retry controls.
- Any inference between `isReady` and startup-tracking `started`.

## Validation

- `git diff --check` passed for RustServer.Web.
- Full frontend validation remains pending because Node/npm is unavailable in
  the current execution environment.

## Decisions

- The Control schema remains the presentation source for milestone labels and
  order; the Web application does not maintain a duplicate milestone catalog.
- Startup completion is represented only by the snapshot's `started` field;
  the client does not infer it from lifecycle status or milestone positions.
