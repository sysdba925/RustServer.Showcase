# Iteration 091 — React Server page adopts Server V7 / Plugins V15 styling

## Goal

Replace the previous tall Server page composition with the compact Server V7
layout and the established Plugins Management V15 Rust visual system while
preserving the Control-plane API interactions and operational safeguards.

## Added and updated

- Reworked `RustServer.Web/src/features/server/ServerPage.tsx` into a connected,
  full-width Start / Stop / Restart toolbar followed by side-by-side Runtime
  and Startup Tracking panels and a wide Server Configuration section.
- Added a Server-specific CSS module with square surfaces, graphite/charcoal
  backgrounds, olive-green positive actions, red Stop action, Roboto Condensed,
  and responsive collapse at narrower widths.
- Added `ServerLifecycleBar` with three separately derived status indicators:
  container running (Control status), Rust readiness (Control status), and the
  final startup milestone (Control startup snapshot). Unknown/unavailable
  telemetry does not produce a false green indicator.
- Kept existing `ServerRuntimePanel`, `StartupTracking`,
  `ServerConfigurationPanel`, and the shared `SoftwarePanel` behavior;
  added opt-in compact presentation variants used only on the Server route.
- Integrated Rust and Oxide install/update buttons inside Runtime, including
  separate installed/running/available values, all existing action guards,
  busy/error states, and expandable running metadata.
- Retained desired/effective configuration values, editable form validation
  and save semantics, and the authoritative accepted-operation Activity panel.
- Added Server page tests for lifecycle placement, buttons, availability
  semantics and update operations; updated the ready Storybook scenario.
- Included a shared font stylesheet reference in `index.html`, matching
  the font already requested by the V15/V7 prototypes.

## Current capability

The Server page retains existing real API-backed actions and displays live
status, Rust/Oxide version details, nine API-defined startup milestones and
configuration. React owns the presentation; no standalone prototype JavaScript
or synthetic operation state was imported.

## Deliberately deferred

- The prototype's floating debug controls, simulated error injection,
  artificial delays and demo data are not part of the production page.
- `Available` denotes the final observed Steam startup milestone for an active
  server session; it is not an independent player-connectivity health probe.
- No changes were made to Control, Versions, Plugins API contracts,
  `ControlPlaneProvider`, Updater or Bootstrap.

## Validation

- Retrieved and compared Server V7, Plugins V15 and their shared prototype
  theme against React Server/Plugins sources and current Control OpenAPI.
- Verified GitHub `develop` source changes and added focused regression tests.
- **Pending local execution:** `npm run check` (lint, typecheck, tests, build)
  and visual review in the real Web deployment/Storybook were not executable
  within the connected GitHub authoring environment.

## Decisions

- Preserve the existing shared ControlPlane state and API ownership; use
  presentation-only `compact` variants so the Overview route remains intact.
- Treat the prototype as a visual UX reference, never an API contract.
- Centralize the Server-only styling in `ServerPage.module.css`, without
  restyling unrelated routes.

## Follow-up — Oxide version lookup while Rust is running

- Confirmed a production-side 500 in `RustServer.Versions` from the actual Rust WebRCON reply `[Oxide] 20:11 [Info] Oxide.Rust Version: 2.0.7830`. Previously, the running-version reader only accepted lines beginning directly with `Oxide.Rust Version:`, causing an unhandled `InvalidOperationException` and loss of *all* Oxide version fields in Web.
- Updated `RustServer.Versions/develop` (commit `e56e699afbf3acfd28960e357fbc4b8f9a456039`) to parse either the raw Oxide version line or the Oxide-prefixed RCON log line, without interpreting unrelated text as a version.
- Malformed or absent `oxide.version` reply text now returns `runningVersion: null` with a concise warning, preserving independent `installedVersion`, `availableVersion`, and `updateAvailable` reporting. Unexpected RCON exceptions still propagate; transport-unavailable behavior remains unchanged.
- Added focused reader regression tests (including the actual received payload and malformed/multiple-line cases) and provider integration test that confirms installed/available fields survive malformed RCON output.
- **No contract/Bootstrap/Web changes:** response JSON shape and endpoint path stay unchanged; this is a Versions backend fix. **Validation pending:** source and regression tests reviewed in GitHub; no .NET SDK was available to run full build/tests or the API contract generator locally. Run those checks before publishing the next Versions dev release, then verify the endpoint with Rust running.
