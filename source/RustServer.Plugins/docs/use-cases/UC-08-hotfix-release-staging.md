# UC-08 — Hotfix Release in Staging

## Goal
Commit selected runtime changes directly into the current Release branch.

## Preconditions
- Environment is `Staging`.
- Source is `Release`.
- Current Release is complete in Plugins and Configs.

## Flow
1. Operator runs the server and reproduces/tests the issue.
2. Runtime changes appear.
3. Operator views Runtime Diff.
4. Operator selects changes.
5. Plugins and Configs changes can be committed independently.
6. Operator confirms Commit + Push.
7. System performs a fresh fetch.
8. System verifies that the current release branches are safe to update.
9. System commits selected changes into the current `release/<version>` branch.
10. System pushes the changes.
11. After all selected repository pushes succeed, System advances Applied
    Baseline to the new commit SHA for each affected repository.
12. System returns a detailed report.

Each commit keeps the operator message as its subject and appends the shared
operation ID, `staging`, and the current `release/<version>` as structured
trailers. If both repositories are selected, both commits use the same
operation ID.

## Result
The same Release branch contains a new hotfix commit.

## Note
No new hotfix branch is created.
Selected Runtime changes are no longer present in Runtime Diff; unselected
changes remain. A failed or partial Commit + Push leaves Applied Baseline
unchanged.
