# Iteration 028 — Web server configuration and world wipe

## Goal

Surface Control's desired/effective server configuration and explicit
stopped-server wipe capabilities in RustServer.Web without coupling them to
automatic lifecycle changes.

## Added and updated

- Server now presents editable desired configuration alongside compact,
  field-level effective values when they differ.
- World now provides next-world seed and world-size editing from the same
  shared configuration resource, and preserves every other desired field in
  its required complete configuration update.
- World now provides independently selectable keep/delete policies for world
  and blueprints, an inline confirmation step, shared operation polling, and
  path-free wipe target results.
- Wipe actions use Control's shared mutation gate and require the server to be
  stopped; the UI never stops, starts, restarts, snapshots, or restores on the
  operator's behalf.
- The configuration and wipe API client coverage, shared-state coverage, and
  presentation tests now cover the published Control contract.

## Current capability

Operators can save desired server settings for the next Rust process, choose
new world generation seed and size, then explicitly submit a confirmed
world/blueprints wipe and observe its result through the existing operation
lookup flow.

## Deliberately deferred

- Snapshot, restore, automatic lifecycle orchestration, operation history,
  cancellation, and plugin-data target discovery remain unavailable.
- Plugin data is sent as an empty plan because Control currently publishes no
  discovery contract for registered plugin targets.

## Validation

- `oxlint` completed with no warnings or errors.
- `tsc -b --pretty false` completed successfully.
- `vitest run` completed successfully: 18 files and 141 tests passed.
- `vite build` completed successfully.

## Decisions

- Desired configuration is one shell-scoped Control Plane resource. Server and
  World use local editing drafts only; neither owns an independent fetched
  configuration store.
- World offers only `keep` and `delete` for built-in Rust targets even though
  the generic Control `WipeAction` also publishes `transform` for trusted
  plugin-data handlers.
