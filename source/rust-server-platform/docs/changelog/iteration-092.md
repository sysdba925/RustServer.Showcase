# Iteration 092 — Overview V5 React layout in Rust UI style

## Goal

Bring the read-only Overview dashboard into the established Rust visual system
of Plugins Management V15 and Server V7. Match the
`docs/ui/prototypes/overview/prototype-v5.html` sections, spacing and
information hierarchy without changing lifecycle/versions APIs or disturbing
the Server Install/Update workflows.

## Added and updated

- `RustServer.Web/OverviewPage` now lays out Server, Software and Activity
  as full-width sections, with Rust and Oxide side by side within Software,
  and responsive single-column layouts on narrow screens.
- Added `OverviewPage.module.css` for the charcoal/graphite rectangular
  panels, condensed type, tight key/value rows, short section headings,
  neutral status labels with colored symbols, and compact activity details.
  It uses the existing Roboto Condensed font loaded by Web.
- Server summary has its status and `Manage server` navigation in the
  panel header. Its authoritative Ready/Starting/Stopped/Maintenance/error
  presentation and compact resolved-startup-milestone count remain intact.
- Replaced Overview's previous mutable `SoftwarePanel` composition with
  the read-only `OverviewSoftwarePanel`. Installed/Running/Available versions,
  Rust branch/build metadata, update-availability indicators, Oxide restart
  hint and independent unavailable/loading states remain visible.
  Install/Update buttons and update callbacks are no longer exposed on
  Overview; they remain available in `Server → Runtime`.
- Added `OverviewActivityPanel`, which retains known operation type/status,
  timestamps, errors/exit codes and accepted-operation fallback when the
  authoritative operation detail is loading or unavailable.
- Added Overview V5 page regression tests and Storybook Starting and
  PartialUnavailable scenarios.

## Current capability

Overview is a read-oriented status and shortcut page without mutation
controls. The Server page continues to own Start/Stop/Restart and the existing
Rust/Oxide install/update actions. All displayed values come from the
existing `ControlPlaneContext` with no new API requests or synthetic data.

## Deliberately deferred

- Prototype-only test toggles, injected failures, demo build values and
  artificial operation controls are not part of the application.
- No Control, Versions, Plugins, Bootstrap, updater, or other Web pages were
  changed. The older `docs/ui/screens/overview.md` wording still mentions
  update actions even though prototype V5 and this implementation locate
  those actions on Server; documentation alignment is a separate task.

## Validation

- Compared the current GitHub `develop` sources with Overview V5 and the
  established Server/Plugins style. Confirmed the current shared control-plane
  operations remain unchanged and no buttons are created on Overview.
- Added tests for read-only interaction, update indicators, navigation,
  startup count, independent version failures, missing optional running
  metadata, accepted operations and operation errors.
- **Pending:** run `npm run check` (lint, TypeScript, Vitest, Vite) and
  inspect the final responsive layout in the actual browser or Storybook.
  Those checks were not executed from the GitHub-only editing environment.

## Decisions

- Preserve a strict distinction between read-oriented Overview status and
  actionable Server Runtime operations.
- Keep Overview's visual styles local to the Overview feature and do not
  modify the shared `SoftwarePanel` used by Server.
- The HTML prototype establishes visual hierarchy, not backend behavior.
