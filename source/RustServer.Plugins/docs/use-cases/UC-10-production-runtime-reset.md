# UC-10 — Production Runtime Diff / Reset to Release

## Goal
Inspect Production runtime changes and restore the server to its Release baseline.

## Preconditions
- Environment is `Production`.
- Baseline points to a Release.

## Flow
1. Operator views Runtime Diff.
2. Runtime commit is not available.
3. Operator reviews changed files.
4. Operator chooses Revert all / Reset to Baseline, or selects specific Runtime Diff entries for Revert selected.
5. UI shows what will be removed, restored, or overwritten in the chosen scope.
6. Operator confirms.
7. System restores the selected managed files, or all managed files for Revert all, from the baseline Release state.
8. Detailed report is shown; unselected files remain changed in Revert selected mode.

## Result
Revert all clears the Production runtime diff. Revert selected clears only the
selected Runtime Diff entries.

## Rule
Production never commits runtime changes to Git.
