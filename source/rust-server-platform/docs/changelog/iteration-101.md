# Iteration 101 — Server Network Access controls

## Goal

Expose the published RustServer.Control NetworkAccess capability on the
existing Server page so an operator can inspect and change public reachability
and the maintenance UDP port without changing Rust lifecycle controls.

## Added and updated

- RustServer.Web Server now includes a Network Access section with Public
  Access, Maintenance Port, and Save controls.
- The section loads persisted settings from Control and applies only the
  published NetworkAccess request fields; its form is refreshed from the
  successful response.
- The UI explains the public-mode effect, the maintenance connection purpose,
  and the risk that changing the maintenance port can interrupt an existing
  game connection using the old port.

## Current capability

An operator can view and update the persisted public reachability mode and
maintenance UDP port from Server. Applying the setting uses the Control API
operation that does not restart Rust or recreate its container.

## Deliberately deferred

- The Server page does not poll NetworkAccess, retain an edit history, or add
  a separate configuration dialog.
- The UI does not add lifecycle behavior, deployment behavior, or network
  capabilities beyond the published Control API.

## Validation

- `npm.cmd run check` passed in RustServer.Web: oxlint, TypeScript checking,
  235 Vitest tests, and the production Vite build.

## Decisions

- NetworkAccess state is local to the Server section because it has no shared
  UI semantic with lifecycle state; it does not extend ControlPlaneProvider.
- The implementation uses only RustServer.Control's published
  `GET` and `PUT /api/server/network-access` contract.
