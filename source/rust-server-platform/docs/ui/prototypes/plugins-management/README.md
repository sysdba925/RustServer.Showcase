# RustServer.Web — Plugins UX layout checkpoint (2026-10-09)

Status: **UX draft approved in principle; not implementation-ready**.

## Objective
Keep a recoverable Azure/AWS-style desktop-first Plugins control panel while backend/API gaps are resolved. This is a layout checkpoint, not permission to implement or modify code.

## Screen layout

- One permanent **Plugins** screen inside the existing application shell.
- Left sidebar: `Overview`, `Server`, `World`, `Plugins` (active). Do not advertise Future Updates yet.
- First/top row: title `Plugins`, current `Environment` (e.g. STAGING), **applied** Source and branch (`Development · develop` or `Release · release/<version>`), and `History` action **on the same row**.
- Second/top command row: `Environment`, `Switch Source`, `Apply Source`. Actions open modal dialogs; they do not blindly execute dangerous operations.
- Main content: **two compact panels side by side** on a wide desktop canvas. No tall stack of duplicated status cards.
  - **Git Repositories** (left): Plugins and Configs rows, local branch, Local ↔ last-known Remote relation, Fetch Git action in panel header, last-fetch information. Relation values from current API: `equal`, `ahead`, `behind`, `diverged`, `unknown`. Display direction and diagnostics; do not equate Fetch with guaranteed sync.
  - **Applied Baseline** (right): applied Source, Plugins commit SHA, Configs commit SHA, time of apply, Runtime status, Runtime Diff action in panel header. Runtime status is derived from `GET /api/runtime/diff`: `status=ready` and `changes.length=0` => Up to date; nonzero => changed files. `status=notInitialized` is a distinct state. Errors => Unknown/error, never Up to date.
- Runtime SSE (`GET /api/runtime/events`) is **invalidation only**; `runtimeChanged` contains no file count or paths. On event, refresh diff or mark that it must be refreshed. Only show a numeric count after successful diff API response.
- Git status alone does not imply the Baseline equals Local/Remote HEAD. Keep distinctions: Remote Git, Local Git, applied Baseline, actual Runtime.
- No separate Runtime summary card or Runtime banner below panels. No repeated Environment and Source cards.

## Modal interactions

- **Environment**: choose Staging/Production and confirm; switching does NOT deploy, start or restart Rust. Production requires a valid Release baseline.
- **Switch Source**: choose Development or Release/version; preview before Apply. Picking a release is only a draft target and must not change displayed applied Source.
- **Apply Source**: source preview, changed files and warnings, stopped-server prerequisite, explicit confirmation, operation tracking/report. Update applied state only after verified success.
- **Fetch Git**: action for both Plugins and Configs; refresh Git status and report actual resulting relations after completion; do not assert Equal automatically.
- **Runtime Diff**: modal with grouped Plugins/Configs files, per-file content diff, selected-file Commit + Push and Revert (not direct top-level commands), commit message and dangerous-operation confirmations. Production disallows Runtime Commit. Committing Git does not update Baseline.
- **History**: reserved placement in first header row. Current API offers known-operation lookup but no durable history list; treat persistent history as a backend capability gap, not an implemented screen.

## API sources of truth

- `RustServer.Plugins/openapi.json`
- `RustServer.Plugins/docs/api-contract.md`
- `RustServer.Plugins/docs/architecture.md`
- `RustServer.Plugins/docs/use-cases/README.md` and UC-01..UC-13
- `RustServer.Web/AGENTS.md`
- `rust-server-platform/docs/ui/` (may have outdated references; compare with current OpenAPI)

Important details: GET `/api/git/status`; GET `/api/baseline`; GET `/api/runtime/diff`; GET `/api/runtime/events`. Exact remaining endpoints, DTOs, enum values, preconditions and outcomes must be verified before implementation.

## Intentional open issues

1. Git table may additionally show compact `Local HEAD` and `Remote HEAD` (not yet approved).
2. Which status to present when Git behind and Baseline differs from applied target.
3. Whether `History` can be functional requires a backend listing/history contract.
4. Full per-file operation UX and detailed source-preview design remain to be discussed.

## Handoff instruction for future agent

Reconstruct the desktop Plugins screen from this specification and the accompanying `prototype.html`, preserve the approved spatial layout, and ask about unresolved elements rather than silently redesigning it. Do **not** begin React implementation until the user expressly approves the UX specification and requests implementation. Do not treat simulated prototype results as API behavior.
