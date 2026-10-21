# Iteration 053 — Plugins management operations

## Goal

Activate the approved V15 Environment, Git, Source Preview, and Source Apply
workflows in RustServer.Web while keeping their safety checks and final state
authoritative to the published RustServer.Plugins contract.

## Added and updated

- Activated Environment selection and explicit confirmation, including the
  persistent Production safety context and visible backend `409` validation
  errors. Environment switching remains separate from Source Apply and Rust
  lifecycle actions.
- Activated Fetch Git and per-repository Git Sync controls. The interface uses
  the observed Plugins and Configs Git status independently and labels the
  backend-selected Sync outcome as Fast-forward, Push, or Merge + Push without
  inventing separate endpoints.
- Added Source selection for Development and complete Releases, contract-backed
  Source Preview, explicit Apply confirmation, and detailed operation-report
  presentation.
- Added feature-local operation tracking that follows a returned `operationId`,
  stops on terminal success/failure/interruption, cancels in-flight requests on
  unmount, shows failure and rollback details, prevents duplicate submissions,
  and refreshes affected Plugins read state after a final report.
- Added deterministic Storybook and focused test scenarios for confirmations,
  API rejection, Git actions, previews, Apply, and operation outcomes.

## Current capability

Operators can change the logical Plugins Environment, Fetch Git, ask the
backend to safely synchronize one eligible repository, select a Source,
inspect its actual Runtime preview, and explicitly Apply it. The screen keeps
Environment, Git, applied Baseline, and Runtime state distinct and only updates
the displayed backend state after a refresh.

## Deliberately deferred

- Runtime Commit, Runtime Revert, runtime file selection, and per-file diff
  dialogs remain unavailable.
- Persistent operation-history listing and pagination remain unavailable even
  though an individual known operation report is shown.
- History and Runtime Diff controls retain their reserved presentation only.

## Validation

- RustServer.Web `npm run check` passed with valid local topology values.
- RustServer.Web `npm run build-storybook` passed without a running backend.
- RustServer.Plugins API-contract verification passed with
  `dotnet run --project tools/RustServer.Plugins.ApiContractGenerator -- --verify`.

## Decisions

- RustServer.Plugins `openapi.json` and `docs/api-contract.md` remain the API
  source of truth. The UI mapping's stale deployment endpoints were not used.
- Bootstrap requires no change: Web already receives and proxies
  `RUSTSERVER_PLUGINS_URL`, and this frontend-only capability introduces no
  service, topology, deployment, or environment-variable requirement.
