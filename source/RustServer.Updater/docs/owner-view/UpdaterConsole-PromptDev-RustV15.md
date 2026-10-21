# Dev Agent Task — Implement Updater Console (RustServer V15 Style)

Repository: `sysdba925/RustServer.Updater`, working branch: `develop`.
Implementation language: English for code/UI labels/documentation unless surrounding project convention requires otherwise.

## Objective

Replace the temporary static `EmergencyPage.Html` UI with the production-worthy **Updater Console**, preserving the backend's existing deployment/recovery semantics. Implement the reviewed **Style A / Operator Console Master–Detail** UX.

**Read first:** `README.md`, `docs/architecture.md`, `docs/api-contract.md`, `openapi.json`, `src/RustServer.Updater.Api/Emergency/EmergencyPage.cs`, `EmergencyController.cs`, `GetEmergencyStateHandler.cs`, `EmergencyUpdateApplicationHandler.cs`, `ApplicationsController.cs`, `OperationsController.cs`, `OperationResponse.cs`, `DeploymentOperationReconciler.cs`, `OperationRecoveryService.cs`, existing `EmergencyEndpointTests.cs`, `tests/emergency-operations-smoke.cjs`, and `scripts/publish.ps1`.

Attached reference files in the prompt context:
- `updater-console-prototype.html`: an **interactive offline mock**. Treat its layout/appearance/interaction as UX target. Mock service versions and simulation buttons are not production requirements.
- `UpdaterConsole-UX-Spec.md`: authoritative approved UX/functional acceptance criteria.

## Hard constraints

1. `RustServer.Updater` is separate from initial rollout. Exactly eight rollout services, **fixed source/build-time order** Auth → Rcon → Control → Versions → Plugins → Snapshots → PluginCatalog → Web. Do not let UI alter order. Preserve existing order and first-install hook behavior.
2. Reuse existing API endpoints; do not create new privileged endpoints or add unneeded API DTOs. Normal per-service Update uses `POST /api/emergency/applications/{name}/update`, Install uses `Prepare` then `Install`, Rollback uses existing previous-installed-release semantics.
3. Keep existing `FileOperationStore` and durable operations untouched. **NO new server-side error history, phase history, local DB, log storage, or diagnostics persistence.** An in-memory per-page error alert remains visible until the user copies/dismisses it or reloads. The operation records that already exist on the server remain available as before.
4. Keep `/emergency` usable when `RustServer.Web` is down/not installed. Bundle HTML/CSS/JS in Updater's own published API. **No external fonts, CDNs, runtime Node/npm, external frontend hosting, or Web dependency**. No heavy JS framework is necessary; TypeScript + Vite (vanilla) is recommended if implementation complexity warrants it. Choose the simplest maintainable build.
5. Preserve privileged executor boundaries, single paired Updater API+Executor release bundle, systemd units, checksum/staging validations, recovery and existing security access policy. In particular do not expose `Updater` publicly; there is currently no application-level auth.
6. Do not attempt release publication, destructive cleanup or live deployment as part of implementation. Do not silently change Bootstrap behavior. If Platform integration is genuinely needed, read `rust-server-platform/AGENTS.md` and its architecture before modifying it; add appropriate central changelog entry only for a material cross-repository boundary change.

## Layout

- Keep the approved **Operator Console Master–Detail layout and all behaviors**, but restyle the entire UI after the user-provided **RustServer.Web Plugins UX V15** reference and the accompanying `updater-console-rust-v15.html`. The visual identity is non-negotiable: 62px full-width `RUSTSERVER / Control Panel` brand bar; Rust favicon in the reddish square tile; black/charcoal backgrounds; `Roboto Condensed` (locally bundled or system fallback); square panel/button corners; olive-green primary buttons and selected rows (`#647e38`); warm amber active/warning; red reserved for errors. Apply consistently to banner, service list, detail, operation stages, history drawer and error states. Keep accessibility and compact sizing. **Do NOT add the Plugin Manager global sidebar/nav or environment/branch features**.
- Header: V15 full-width RUSTSERVER brand with the Rust icon and `/ Control Panel`, then `UPDATER CONSOLE`, backend-owned read-only Development/Production channel, connection and Refresh. Source V15 favicon is an external URL in a prototype; production must use a **locally packaged approved icon**, with visible `R` fallback, without external network requests.
- Top: single rollout progress bar (`installed completed` / 8) with active service name, stop/failure/retry state; no fake per-operation percentages.
- Left: 9 services visible with order above, Updater pinned/separated at top; show installed version + runtime health status or separate recent-operation indicator. No full-height nine-card scrolling at desktop size.
- Right: single selected service details, installed/latest version, release selector, Install/Update/Rollback actions, **one active operation stepper**. During rollout move active operation through its stages, then move to next service. Preserve intentional manual selection.
- Bottom: entry to a right-side scrollable history drawer, operation detail view, full diagnostic output and Copy full report.
- No page-wide modal spam: on failure automatically reveal affected service's error in the right pane with `Copy error`, `View full details`, and optionally `Dismiss`.

## Error treatment

- All errors from accepted or rejected user actions should remain in the current page until copied/dismissed/reload; hide only the UI alert on acknowledgment, **do not modify** operation record files or API status.
- Use a concise error summary inline plus full error string, no arbitrary truncation that prevents copying.
- Detailed report should include operation UUID, app, action, status, last step, known versions, timestamps/duration, activation/rollback outcome, exit code and provisioning output when present. Use safe text rendering (`textContent` / DOM text), not HTML injection. Long logs scroll inside the drawer; selection and copying work.
- Backend only has the **latest** durable `Step`, not a persisted timeline. Derive currently rendered stage states cautiously from this field. Do not imply all steps were separately persisted. Unknown steps remain visible as text.
- A service can be **Running** while its latest Update failed: keep runtime state and operation outcome independent.

## Rollout behavior

- Confirm backend state before first rollout or retry. Check selected exact release availability for each missing app; do not start partial preparation if preflight says a needed release is unavailable.
- Use the existing Prepare then Install endpoints for each not-installed service in fixed order. Each accepted operation returns `202 {operationId}`; poll `GET /api/operations/{id}` and wait for `Succeeded`, `Failed`, or `Interrupted` before next command.
- Update single shared operation stepper from current operation's `step` / `status`. Advance the global bar only when an app's Install is confirmed and installed state refreshed.
- On first failed operation, stop the rollout loop, show error immediately, offer `Retry rollout`. On retry **re-read** server state and skip already installed applications; do not reinstall them and do not replay a previously accepted operation. Closing/reloading the page stops *future browser-controlled* scheduling but accepted backend operations continue. Handle previously active operations conservatively when retrying.
- If every rollout app is installed, don't show a misleading Rollout button; show compact completed/ready state. Updater never counts toward these eight.

## Updater SelfUpdate, distinct UX

- The actual backend classifies Updater updates as `SelfUpdate` and delegates to the paired bundle Executor. Preserve this.
- After `POST /api/emergency/applications/RustServer.Updater/update` returns the durable UUID, track it with GET `/api/operations/{id}`. While API is restarting, display `Restarting / Reconnecting`, retry **GET only every 2 s**. DO NOT re-POST or assume a connection failure means rollback.
- After recovery, verify operation terminal outcome and actual installed/active Updater version, including `deployment.activeVersion`/`rolledBack` where present. Distinguish successful new version, restored previous release, failed rollback, and truly unknown state.
- At 90 seconds with no answer, show `Result unknown` with a **manual Retry connection** that only fetches state; don't call `Update` again. A browser refresh may discard transient alerts; use existing durable `/api/operations` records to rediscover ongoing state when appropriate, not new persistence.

## API specifics to keep correct

- `GET /api/emergency` returns channel + app snapshots + `availableVersions[]` and per-app `availableVersionsError`. Treat backend list as authoritative; channel is not editable in UI.
- `POST /api/applications/refresh` validates/reloads registry, local status and latest snapshot. `GET /api/emergency` performs release discovery. Decide and document accurate Refresh behavior; avoid per-second GitHub release scans.
- `GET /api/operations?limit=50` returns recent **durable** records; `GET /api/operations/{id}` gets full detail. `status` is Pending/Running/Succeeded/Failed/Interrupted. `step` is text, not a server progress percentage.
- `deployment.rolledBack`, `deployment.activeVersion`, provisioning output and exit code may be present in actual API payload. Read actual serialization/model; omit missing optional details gracefully.
- Do not add frontend-local reordering or a production/development channel toggle.

## Build / hosting

- Move UI out of `public const string Html`; use dedicated maintainable source files and a reproducible local build. All branding assets and fonts, where bundled/used, must ship with the Updater bundle and function offline. Embed compiled static files under API `wwwroot` or equivalent. Ensure `/emergency` serves the console index with correct base URLs (e.g. `/console/assets/...`). Configure ASP.NET Core static file serving; no runtime build on Ubuntu VPS.
- Ensure Visual Studio `dotnet publish` and `scripts/publish.ps1` assemble the UI **into the existing Updater release bundle**. Validate paired bundle invariants and preserve all executor systemd assets. Test that self-update/rollback serve the console successfully after switch.
- Update README/architecture/operator docs and `openapi.json` documentation only where applicable; if OpenAPI endpoint semantics do not change, prefer documentation clarification over contract changes.

## Tests / acceptance

1. UI shows 9 service entries in fixed order, no drag/drop or page-height nine cards.
2. App selection and release selection work; all actions are disabled correctly during an active conflicting operation.
3. Sequential eight-service Rollout: successful stages move to next app, progress counts confirmed installed applications, final Ready state.
4. Failing Prepare/Install: inline error visible immediately, copyable full details, no further Rollout command; Retry discovers installed state and resumes without reinstall.
5. Error acknowledgment/refresh clears ephemeral UI alerts, but existing operation API records are unchanged.
6. Operation details drawer supports failed/successful/Interrupted and optional provisioning output; safe text rendering and copy.
7. SelfUpdate: mock API outage and reconnection; GET-only 2s polling, no duplicated POST, correct success/rollback/unknown states.
8. `GET /emergency` returns the console and all referenced local assets are included in the published Updater bundle. Validate both Development and Production packaging entrypoints (without publishing a release).
9. Existing backend tests remain passing. Replace old brittle assertions for literal HTML with proper static-asset/behavior tests where possible. Include a lightweight browser/DOM test for new interaction behavior.
10. Run `dotnet test RustServer.Updater.slnx`, relevant frontend tests/build, and `git diff --check`; report failures or unrun checks honestly. No release publish or runtime side effects.

## Work order and report

1. Inspect and map current APIs and packaging. Briefly state planned changes and any incompatibility found before coding.
2. Implement static frontend/components with the approved Master–Detail UX and the **new RustServer V15-styled prototype**. Preserve all interactions, error behavior and stepper animation as-is; change styling only where relevant.
3. Connect real APIs, transient error alerts, rollout and self-update state machines.
4. Integrate publishing/static hosting, update tests/docs.
5. Report changed files, key UX behavior, build/test results, residual limitations and screenshot/preview evidence where feasible.

**When this prompt is given to the development agent, implement the changes in the repository.** Do not publish a release, push to production, or run live deployment. Report the resulting commit/PR only after tests and the user's normal review workflow.

## Latest visual revision (authoritative)

- Supersedes the older graphite/teal look and earlier generic Style A mockup **only for visual styling**. Layout, states, interactions, rollout order, APIs, and error persistence policy remain unchanged.
- Source design: `prototype-v15(2).html` (user's Plugins V15 HTML). Adapt its `.brand`, `.rust-logo-slot`, `.rust-logo-tile`, `.btn`, `.badge`, `.panel`, modal/history treatments, and its final olive-green hover/selection overrides. **No side navigation.**
- Final reviewed visual prototype: `updater-console-rust-v15.html`. It preserves the prior prototype's exact interactive JS, with styling and brand markup changed. Match its current screen, error screen and self-update reconnect screen.
- The V15 HTML references the Rust official favicon over HTTPS for design-time preview. **Production must serve the same approved artwork as a local static asset**, with fallback rendering when missing; never rely on the original external URL in deployed emergency mode.
