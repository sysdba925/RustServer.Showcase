# Iteration 056 — Plugins operation history

## Goal

Provide the approved Stage 2.4 Plugins operation-history workflow so operators
can browse persistent operation summaries and inspect the existing full report.

## Added and updated

- Activated the Plugins History control and added the V15 history dialog.
- Connected compact operation-history pages at the approved 10, 50, and 200
  sizes, preserving opaque cursor continuation.
- Displayed published operation status, type, timestamps, changed-file count,
  and the supported loading, empty, error, retry, and interrupted states.
- Reused the existing full operation-report dialog for a selected history row
  and refresh history whenever it is opened.
- Added History stories and retained behavior, API-contract, mutation, and
  operation-safety tests while removing two tests duplicated solely by visual
  stories.

## Current capability

Operators can review persisted Plugins operations in newest-first cursor pages,
change the page size without client-side sorting, and open the corresponding
full report. History uses only the published compact-list and detail-report
capabilities; it does not invent operation details or live progress.

## Deliberately deferred

- History mutations, cancellation, and live progress remain unavailable because
  they are outside the published contract and this iteration's scope.
- Runtime Management and other Plugins workflows remain separate capabilities.

## Validation

- RustServer.Web `npm run check` passed lint, typecheck, 188 tests, and the
  production build with the local service topology configured.
- RustServer.Web `npm run build-storybook` passed.
- RustServer.Plugins contract verification passed with
  `dotnet run --project tools/RustServer.Plugins.ApiContractGenerator -- --verify`.

## Decisions

- The compact list is used strictly for summaries; the existing detail endpoint
  supplies the full report on demand.
- Cursors remain opaque, and the client does not synthesize ordering, summary
  data, or lifecycle states.
- Storybook remains the visual-state source for the removed duplicate tests;
  behavior and safety coverage remains in the test suite.
