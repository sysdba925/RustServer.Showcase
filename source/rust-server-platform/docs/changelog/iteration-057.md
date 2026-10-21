# Iteration 057 — Runtime Diff clarity and committed Baseline advancement

## Goal

Make Runtime Diff review readable at line level and ensure a successful
Runtime Commit + Push advances the persisted Applied Baseline without requiring
a redundant Apply Source operation.

## Added and updated

- RustServer.Web now renders Runtime Diff text content as a unified
  line-by-line comparison: added lines are green with `+`, removed lines are
  red with `−`, and unchanged lines remain neutral. Added, modified, and
  deleted text files use the existing Runtime Diff Content API; binary content
  remains explicitly non-text.
- RustServer.Plugins now persists a new Baseline only after every selected
  repository commit has pushed successfully. It advances exact SHA only for
  affected repositories and preserves the prior SHA for unaffected ones.
- Commit reports now include a `persistBaseline` step. Failed, partial-push,
  cancelled, or Baseline-persistence-failed Runtime Commit operations leave the
  previous Baseline unchanged; already pushed commits are never force-rolled
  back.

## Current capability

Operators can inspect an exact text delta before selecting Runtime changes. A
fully successful Commit + Push immediately removes selected changes from
Runtime Diff and refreshes the displayed Applied Baseline. Unselected Runtime
changes remain visible.

## Deliberately deferred

- The Runtime Diff Content API remains a file-content API; it does not publish
  precomputed presentation diff lines or a binary preview.
- No distributed rollback is introduced for an already pushed repository.

## Validation

- RustServer.Plugins Release build completed with zero warnings and errors.
- RustServer.Plugins Release test suite passed: 91 tests.
- RustServer.Plugins OpenAPI contract verification passed with
  `dotnet run --project tools/RustServer.Plugins.ApiContractGenerator --configuration Release --no-build --no-restore -- --verify`.
- RustServer.Web `npm run check` passed: lint, typecheck, 191 tests, and
  production build.

## Decisions

- Applied Baseline is the persistent exact Git reference for the managed
  Runtime comparison. A successful Commit + Push may advance it only after all
  selected pushes and its own persistent save have succeeded.
- Bootstrap requires no change: the work introduces no service, topology,
  configuration, filesystem-layout, permission, readiness, or deployment
  change.
