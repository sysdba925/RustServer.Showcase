# UC-06 — Switch Staging from Development to Release

## Goal
Move Staging from Development to a selected Release.

## Preconditions
- Environment is `Staging`.
- Current Source is `Development`.

## Flow
1. Operator fetches or uses the current known Release list.
2. UI shows complete Releases.
3. Operator selects a specific Release.
4. System checks for Runtime Diff.
5. If Runtime Diff exists, UI warns that those changes will be lost by Apply.
6. Operator can review the diff.
7. Operator stops the Rust server.
8. Operator confirms the switch.
9. System performs a fresh fetch.
10. System validates the selected Release in both repositories.
11. System performs the linked checkout + apply operation.
12. Baseline becomes the selected Release.
13. Detailed operation report is shown.

## Result
Staging is now on the selected Release.
