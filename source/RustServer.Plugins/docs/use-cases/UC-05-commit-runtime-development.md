# UC-05 — Commit Runtime Changes from Development

## Goal
Take selected runtime changes from Staging and commit them to Development.

## Preconditions
- Environment is `Staging`.
- Source is `Development`.
- Baseline exists.

## Flow
1. Operator views Runtime Diff.
2. Operator selects changes to keep.
3. Changes may be selected separately for Plugins and Configs.
4. Operator starts Commit + Push.
5. System performs a fresh fetch.
6. System verifies that remote state is safe for commit.
7. System commits selected changes to `develop`.
8. System pushes the commit(s).
9. After all selected repository pushes succeed, System advances Applied
    Baseline to the new commit SHA for each affected repository.
10. System returns a detailed report.

Each commit keeps the operator message as its subject and appends the shared
operation ID, `staging`, and `development` as structured trailers. If both
repositories are selected, both commits use the same operation ID.

## Result
Selected runtime changes are stored in remote Development and no longer appear
in Runtime Diff. Unselected runtime changes remain visible. A failed or partial
Commit + Push leaves the Applied Baseline unchanged.
