# UC-09 — Switch Production Release

## Goal
Move Production to any complete Release.

## Preconditions
- Environment is `Production`.
- Only Release Source is available.

## Flow
1. Operator opens the Release list.
2. Operator selects any complete Release.
3. Release may be newer or older than the current Production Release.
4. System shows Preview.
5. Runtime Diff, if present, is shown and will be lost after Apply.
6. Operator confirms.
7. System performs a fresh fetch and validation.
8. System applies the selected Release.
9. Baseline is updated.
10. Detailed report is shown.

## Result
Production is on the selected Release.

## Note
Upgrade and rollback are the same use case.
