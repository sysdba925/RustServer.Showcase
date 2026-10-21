# Iteration 039 — Apply a validated Source to Runtime

## Goal

Complete the first RustServer.Plugins Git-to-Runtime mutation workflow with a
safe, reportable Apply Source operation.

## Added and updated

- Added `POST /api/source/apply`, using the same Target Source request model as
  Preview and returning an operation id after synchronous completion.
- Apply now holds the existing exclusive guard, requires a stopped Rust server,
  performs fresh Fetch for both repositories, and revalidates the Target Source
  from the fetched remote-tracking refs.
- Added safe target-branch preparation: clean worktrees, canonical tracking,
  create-from-remote/equal/fast-forward-only paths, with ahead and diverged
  branches rejected.
- Runtime Apply reads exact validated Git commits as bytes, writes additions
  and modifications, deletes obsolete managed files, then persists an exact
  Baseline only after Runtime success.
- Extended the existing operation report with changed-file counts and details,
  failure context, rollback outcome, and final Git/Baseline state for Apply.

## Current capability

An operator can confirm an Apply for Staging Development, Staging Release, or
Production Release. The service freshly validates the source, transitions both
workspaces as one operation, and makes managed Runtime files exactly match the
target. If Runtime apply or Baseline persistence fails, it attempts scoped
Runtime and workspace restoration and exposes the resulting report.

## Deliberately deferred

- No Release-specific rollback endpoint or navigation feature was added:
  applying an older Release remains the future standard Apply workflow.
- Runtime Commit + Push, Runtime Revert, and Plugin Updater are still out of
  scope.

## Validation

- RustServer.Plugins Release build passed with warnings as errors.
- RustServer.Plugins tests passed: 28 total, including Apply integration tests
  for fresh Fetch, exact binary-safe add/modify/delete, Baseline SHAs and
  report capture, plus Baseline-save failure rollback and guard release.

## Decisions

- ApplySourceHandler is the workflow owner and calls RemoteGit, LocalGit,
  Runtime, Baseline, Guards, and operation reporting directly; it does not
  invoke FetchGitHandler or introduce a queue/workflow framework.
- Platform topology and Bootstrap remain unchanged: Apply adds no URL, port,
  bind host, configuration key, filesystem layout, or deployment dependency.
