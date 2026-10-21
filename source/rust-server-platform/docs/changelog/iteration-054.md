# Iteration 054 — Plugins Runtime Management

## Goal

Deliver the approved V15 Runtime Diff workflow in RustServer.Web so operators
can inspect managed Runtime changes, commit selected Staging changes, or
explicitly revert selected or all managed changes through RustServer.Plugins.

## Added and updated

- Activated the Applied Baseline panel's Runtime Diff action with grouped
  Plugins and Configs entries for added, modified, and deleted managed files.
- Added lazy per-file Baseline-to-Runtime content comparison, including safe
  presentation when a content side is binary or missing.
- Added selected-file Commit + Push with a required operator message and an
  explicit confirmation. Production continues not to offer Runtime Commit.
- Added destructive confirmations for Revert selected and body-less Revert
  all. Revert selected submits only the selected repository/path pairs;
  Revert all deliberately uses the published request form without a body.
- Reused persistent Plugins operation tracking and reports for all Runtime
  mutations, then refreshes the affected observable Git and/or Runtime state.
- Added contract-client, focused interaction, and Storybook coverage for the
  Runtime Diff and mutation workflows.

## Current capability

Operators can view the actual managed Runtime diff while Rust is running, read
an individual file's available content comparison, and then choose an explicit
mutation. The UI states that Rust must be stopped before Commit + Push or
either Revert operation; backend preconditions, selection validation, Git
safety checks, reports, and final state remain authoritative.

## Deliberately deferred

- Operation History remains out of scope for this stage, including its UI even
  though the header placement is reserved.
- Runtime SSE invalidation is not connected in this iteration.

## Validation

- RustServer.Web `npm run check` passed with valid local topology values:
  lint, typecheck, 185 tests, and the production build.
- RustServer.Web `npm run build-storybook` completed successfully.
- RustServer.Plugins API-contract verification passed with
  `dotnet run --project tools/RustServer.Plugins.ApiContractGenerator -- --verify`.

## Decisions

- Runtime Diff stays a dedicated Plugins workflow: it compares persisted
  Baseline commits to Runtime files and does not infer a Baseline from local or
  remote Git state.
- Commit sends only the published selection identity (`repository`, `path`),
  never frontend display metadata such as the change classification.
- Bootstrap requires no change: this frontend-only capability uses the
  existing RustServer.Plugins proxy and public HTTP contract.
