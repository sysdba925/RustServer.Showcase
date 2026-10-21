# Iteration 032 — Plugins runtime-change workflows

## Goal

Provide the UC-05 through UC-07 Staging operator workflow for reviewing exact
deployment-baseline runtime changes, importing selected Development-baseline
changes, or explicitly discarding selected changes.

## Added and updated

- Added the `/plugins/runtime-changes` surface, overview navigation, and a
  deploy-conflict link when the backend reports Managed runtime changes.
- Added published Staging runtime comparison, text-diff, Import, and Discard
  API clients and DTOs. Runtime selections retain server-issued opaque tokens.
- Added baseline context, per-path selection, nullable deployed-baseline/runtime
  diff presentation, Import review/results, and a destructive Discard review.
- Added runtime workflow and API-client coverage, including stale-comparison,
  empty, unavailable, source-eligibility, and selected-token request cases.

## Current capability

Operators can review current Staging managed changes against the exact deployed
source SHAs. Development baselines allow selected changes to be committed as
one logical Import operation; Release and unknown baselines do not. Discard is
an explicit selected-path Staging operation for documented Development and
Release baselines. Successful Import refreshes the comparison and makes clear
that it does not change the deployed baseline.

## Deliberately deferred

- Environment discovery/change, production runtime changes, history, polling,
  catalog/configuration editing, automatic Import-to-Discard or deploy, and
  lifecycle/restart behavior remain outside this slice.

## Validation

- `git diff --check` completed successfully.
- `dotnet run --project tools/RustServer.Plugins.ApiContractGenerator -- --verify`
  completed successfully against the current Plugins OpenAPI publication.
- `npm`/Node.js are not installed or available on this environment's PATH, so
  tests, lint, typecheck, build, and local route serving remain to be run.

## Decisions

- Runtime Changes remains a dedicated feature-local workflow, distinct from
  Incoming Changes. Its `source` API field remains `string`; the UI fails
  closed for unknown sources.
- A runtime diff or Import/Discard `409` never retries. It clears token-bearing
  transient state, closes stale review/diff UI, and refreshes the authoritative
  comparison.
