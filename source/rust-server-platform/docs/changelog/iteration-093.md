# Iteration 093 — React World page adopts World V1 / Plugins V15 Rust styling

## Goal

Apply the selected `docs/ui/prototypes/world/prototype-v1.html` composition
to the real `RustServer.Web` World route, matching the compact rectangular
charcoal/olive visual language already used by Plugins Management, Server and
Overview. Preserve the existing configuration, wipe and snapshot workflows,
including their safety and error-handling behavior.

## Added and updated

- `WorldPage.tsx` now has the V1 asymmetric two-column arrangement:
  Next World above Wipe on the left, Snapshots as the taller panel on the right.
  The grid stacks responsively for narrower widths.
- Added `WorldPage.module.css` for square-edged panels, condensed typography,
  dark form inputs and tables, olive-green primary actions, red destructive
  actions, concise status markers and inline operation notices.
- Added opt-in `compact` presentation to `NextWorldConfigurationPanel`,
  `WipePanel`, and `SnapshotsPanel`. Existing default presentation and
  core operation logic were retained.
- Next World retains desired/effective Seed and World Size, and continues
  submitting the *full* desired server configuration payload without
  restarting, stopping or wiping Rust.
- Wipe retains separate World/Blueprints choices, confirmation, clear
  disabled/error states and actual per-target results. Added a visible
  "Latest wipe result" section with truthful empty state.
- Snapshots retains Fast/Consistent creation, restore/delete confirmation,
  list loading/error/empty states, server-stopped restrictions and asynchronous
  operation polling. Added the V1 stored-snapshot count, local operation
  summary and a real Refresh action that calls the existing list API.
- Wipe's header now reflects the stopped-server requirement and applies an
  additional status-derived UI preflight if the shared guard is unavailable;
  the Control backend remains the authority for accepting or rejecting wipes.
- Added regression tests for page placement, configuration save, wipe
  confirmation/result presentation and the snapshot Refresh and safety guards.
  Added a stopped-server Storybook state alongside the existing Ready/Blocked.

## Current capability

The World route presents all three pre-existing API-backed workflows in one
compact layout. The new Refresh action reloads snapshots; no simulated data or
prototype-only controls are used. The existing parent `ControlPlaneContext`
and snapshot API client/polling logic remain the single source of live state.

## Deliberately deferred

- The prototype's debug panel, injected HTTP failures, artificial delays,
  synthetic snapshots and simulated operation states are not production UI.
- This change does not introduce world discovery, new backend endpoints,
  snapshot scheduling, or a separate operation-history service.
- No changes to Control, Snapshots, Versions or Plugins HTTP API contracts,
  Updater, Bootstrap, Server or Overview components are required.
- The earlier `docs/ui/screens/world.md` still describes a placeholder and
  was not updated here; align the UI contract separately when explicitly
  authorized to edit Platform UI documentation.

## Validation

- Compared World V1 and shared prototype style with the React World
  components, existing Server/Overview CSS conventions and World use cases.
- Inspected all relevant GitHub `develop` source edits and added focused
  tests for the previously working actions and the new V1 presentation.
- **Pending local execution:** `npm run check` (lint, TypeScript, Vitest,
  production build) and browser/Storybook visual review were not run from
  the connected GitHub-only editing environment.

## Decisions

- Reuse the established React components and their API orchestration rather
  than copying prototype JavaScript or creating a second backend state model.
- Style the World feature with its own CSS module and opt-in compact variants,
  preventing unintentional restyling of other routes.
- Enforce stopped-server constraints via both visible UI preflight and the
  existing backend contracts; UI status never authorizes an unsafe operation.
