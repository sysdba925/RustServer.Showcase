# RustServer.Updater — Updater Console UX Specification

Status: **Agreed prototype — Master–Detail, RustServer Plugins V15 visual identity**
Source of truth: [RustServer.Updater, branch `develop`](https://github.com/sysdba925/RustServer.Updater), `openapi.json`, `README.md`, `docs/architecture.md`, `Emergency/EmergencyPage.cs`.
Prototype: `updater-console-rust-v15.html` (mock data; no backend connections). Design reference: user-provided `prototype-v15(2).html` (RustServer.Web Plugins UX V15).

## Goals

Replace the temporary `EmergencyPage.Html` C# constant with a **permanently usable, compact, standalone operator UI** for routine install/update/rollback/recovery. This page must continue to work when `RustServer.Web` is not installed or unavailable. Keep the existing `/emergency` URL and deployment security boundaries.

## Layout and appearance

- **Approved visual identity: RustServer.Web Plugins UX V15**. Keep the previously approved Operator Console **Master–Detail layout and interactions**, but apply the uploaded plugin-manager prototype's Rust visual language consistently: almost-black backdrop (`#0d0d0c`), dark charcoal panels (`#181817`), muted warm text, thin separators (`#35332f`), rectangular/square corners, uppercase condensed typography (`Roboto Condensed`, with suitable system fallback), olive positive/action/selection fill (`#647e38`, hover `#87a94c`), amber for active/warning (`#e0ad57`), red only for failures (`#e46a55`, `#3c1713`), no glowing borders.
- Full-width **brand header from V15**: official Rust favicon in red square tile (`#cf432c`) inside the 62px dark gradient logo slot; `RUSTSERVER` / `Control Panel` typography and 62px bar. Keep it independent of the main page's max-width. Original prototype loads `https://rust.facepunch.com/favicon.ico` with fallback `R`; for production, bundle the approved icon locally with the Updater static assets and retain the fallback, so emergency mode never needs an internet connection for its header.
- Below the brand, show `UPDATER CONSOLE`, the read-only **Development/Production** release-channel indicator, connection status and Refresh. **Do not add the Plugin Manager's global left navigation** (`Overview / Server / World / Plugins`) or its branch/environment selectors. The only left-side list remains the **nine updater services**.
- Top: **one global initial-rollout progress panel**, 0–8 completed applications. When complete, show compact ready state. During deployment, show current application. Do not create nine long panels or require page-level scrolling at typical desktop viewport sizes.
- Left: **nine compact service rows**. `RustServer.Updater` is separate at top, then the **eight rollout applications in the fixed compile-time order**:
  1. RustServer.Auth
  2. RustServer.Rcon
  3. RustServer.Control
  4. RustServer.Versions
  5. RustServer.Plugins
  6. RustServer.Snapshots
  7. RustServer.PluginCatalog
  8. RustServer.Web
  No drag/drop, reordering, or user-specified order. Change the build-owned list only when required by platform changes.
- Right: **one selected service detail view** containing runtime status (separate from last deployment result), installed/latest versions, release selector, Install/Update/Rollback controls, and exactly one operation stepper. Under initial Rollout, selection follows the active app unless the operator explicitly chooses something else; never yank manual selection.
- Bottom: small `Operation history` entry opening a scrollable right-side drawer. Selecting an operation shows its technical details and a Copy full report button; no giant history list on the main page.
- Responsive: compact service navigation and a single detail panel, with accessible focus and status announcements.

## Operation lifecycle

- Normal install from initial rollout is sequential **Prepare → Install** using the existing endpoints. Backend returns separate **durable operation IDs** for Prepare and Install. Visually treat the two as one application's progress, but keep their identity in the detailed technical view. Do not add new privileged rollout endpoints.
- Normal Update uses `POST /api/emergency/applications/{name}/update` and polls `GET /api/operations/{id}`.
- Rollback uses `POST /api/applications/{name}/rollback` with no target version by default (existing semantics). Do not treat an arbitrary GitHub release as a rollback.
- Operation phases/steps are derived from the backend's existing `status` and `step`: resolving, downloading, verifying, extracting/staging, executor activation and readiness. Use a stepper. Do **not** invent progress percentages, server step history, or timestamps the API does not provide. Unknown step strings must remain legible, not become a fake success.
- The **global rollout bar means apps completed out of 8**, not bytes or an invented percent of the current operation. Updater is excluded from both denominator and sequence.
- When initial rollout fails, stop submitting additional operations and show the error on the affected service. The button becomes **Retry rollout**. On retry, query the actual backend state and skip already installed entries, proceeding in the **same fixed order**. Do not reinstall completed services. Close/reload behavior: browser orchestrator stops scheduling remaining apps; accepted backend operations continue. Retry checks backend state again.

## Error model — intentionally ephemeral UI alerts

- On the active screen, errors are visible immediately in a clear warning section of the right detail area and as a badge in the affected service row/top progress area. Do not require opening history to discover a failure.
- `Copy error` copies the plain failure text and acknowledges/hides this UI alert; `View full operation` opens the technical detail drawer. A simple `Dismiss` is optional. Errors remain displayed in the current page **until copied, dismissed, or refreshed**. Reload clears these UI alerts.
- Detailed report: operation ID, application, action, status, last step, previous/target/actual versions if available, started/finished/duration, exit code, error and provisioning output when available. All text must wrap safely, be scrollable in the drawer, and be copyable. Hide missing optional fields visually; include them as `Unavailable` if a stable full-report text format is desired.
- **Do not add any new server persistence** for UI alerts, stage traces or diagnostic logs. The pre-existing durable operation store (`FileOperationStore`) and `/api/operations` endpoints must remain intact and continue retaining their existing records; do not delete or downgrade them. The UI history can display those existing records, but in-page alert acknowledgment is separate from persisted operation data.
- Runtime health, installation state and last operation result are different concepts. A failed update with an intact installed version is **not** equivalent to a stopped runtime. Distinguish executor-verified rollback success/failure where reported.

## SelfUpdate — separate from initial rollout

- `RustServer.Updater` is already installed when the page is available. It is NEVER part of the eight-service rollout.
- Update targets it through the existing `POST /api/emergency/applications/RustServer.Updater/update`, yielding a `SelfUpdate` operation and a durable UUID. Update continues through verified preparation, the paired API+Executor bundle and root-owned executor.
- API becomes briefly unavailable when the Updater restarts. **Only reissue GET** `/api/operations/{id}` during reconnection (every 2 seconds); never repeat the accepted POST.
- Show `Restarting Updater` → `Reconnecting` without reporting a network error as deployment failure. After the API returns, get terminal operation status, deployment result and active version; communicate whether new release is live, old release restored, or outcome genuinely unknown.
- A 90-second initial reconnect timeout may be retained, but expiry means **Result unknown**, not `Update failed`. Provide a manually triggered `Retry connection` (GET only); do not infer a rollback from unavailability. Support returning to the operation after a page reload using the existing operation list if possible without adding server state.
- Do not change transaction reconciliation, service unit, privileged executor, recovery or publishing invariants for a UX feature.

## Refresh and selection

- The read-only channel is backend-owned; never add a channel switch to this UI.
- `GET /api/emergency` provides the available channel-filtered release list. `POST /api/applications/refresh` refreshes local runtime snapshots and registered application state. Expose a **Refresh** control with coherent semantics: communicate what is refreshed and don't silently represent a stale view as fresh. Avoid frequent broad GitHub release scans during per-operation polling.
- Release selector choices come only from API-provided channel-allowed versions. Preserve exact tags. Prefer newest permitted release as default but allow explicitly selecting available versions where API permits. Clearly warn if selecting a version older than installed.

## Technical boundaries

- Retain the `/emergency` URL; serve static local HTML/CSS/JS from inside Updater's own published API release. No CDN, external fonts, external favicon requests, separate server or dependency on `RustServer.Web` at runtime. (The standalone **visual prototype** preserves the reference V15 favicon URL because it is the exact user-supplied icon source.) A small TypeScript/Vite build is acceptable with zero Node dependency on production host. Static path or base URL must work from `/emergency`.
- Preserve existing OpenAPI contract and operation semantics; avoid changing endpoint contracts without a demonstrated necessity and accompanying contract/tests/docs updates.
- Review the already-present API unauthenticated administrative surface: **no public exposure**. Existing network/reverse-proxy restrictions remain mandatory. Do not add new secrets to UI/logs or expose privileged paths.
- Respect the release bundle `api/ + executor/ + systemd/` and the `scripts/publish.ps1` packaging path. Ensure static assets are in `dotnet publish` and consequently inside the immutable Updater package, including during self-update and rollback.

## Validation checklist

1. 9 service rows visible on desktop with no nine-card scroll.
2. Fixed order: Updater, Auth, Rcon, Control, Versions, Plugins, Snapshots, PluginCatalog, Web.
3. Rollout sequentially prepares/installs eight apps; global bar advances only after confirmed installs.
4. A failed rollout immediately shows error; Retry skips actually installed apps and continues in order.
5. Error text copy and full report copy both work. UI acknowledgment never modifies server records.
6. Refresh of browser clears UI errors; server's existing Operation history still behaves unchanged.
7. Self-update API disconnect shows reconnecting, GET polling every 2 seconds; no duplicate POST; clear unknown/rollback/success outcomes.
8. No invented numeric operation progress. Unknown Step is displayed safely.
9. No external assets or runtime dependency on Web/Node. Verified publish artifact includes console assets **and an approved locally served Rust header icon**.
10. Existing Updater tests and smoke coverage updated, no deployment regressions.
