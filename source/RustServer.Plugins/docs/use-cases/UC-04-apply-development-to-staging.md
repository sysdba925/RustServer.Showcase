# UC-04 — Apply Development to Staging

## Goal
Bring Staging server managed files to the current Development state.

## Preconditions
- Environment is `Staging`.
- Target Source is `Development`.
- Git is healthy.

## Flow
1. Operator selects Development.
2. Operator opens Preview.
3. System shows added, modified, deleted, and moved/renamed files when detectable.
4. Operator confirms Apply.
5. System performs a fresh Git check/fetch.
6. System validates final Development state.
7. System applies the exact managed file set to the server.
8. Obsolete managed files are removed.
9. Baseline is updated.
10. Detailed operation report is shown.

## Result
Server baseline and managed Oxide files match Development. Plugin reload behavior is not evaluated.
