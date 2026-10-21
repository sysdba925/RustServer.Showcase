# UC-07 — Switch Staging between Releases

## Goal
Move Staging from one Release to another.

## Flow
1. Operator opens Release list.
2. Operator selects any complete Release.
3. Selection may be newer or older than the current Release.
4. System previews the change.
5. If Runtime Diff exists, UI warns that it will be replaced.
6. Operator reviews the diff if needed.
7. Operator confirms.
8. System performs fresh Git validation.
9. System applies the selected Release.
10. Baseline is updated.
11. Detailed report is shown.

## Result
Staging runs the selected Release.

## Note
Upgrade and rollback use the same operation.
