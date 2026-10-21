# Iteration 040 — Harden Source resolution and Apply cancellation

## Goal

Correct Stage 13–14 edge cases so a Target Source is always an exact remote
tracking branch and a canceled Apply remains a completed, diagnosable operation.

## Added and updated

- Target Source resolution now verifies literal
  `refs/remotes/origin/develop` or `refs/remotes/origin/release/<version>`
  refs; release input cannot be interpreted as a Git revision expression.
- Invalid Release request values now return `400 InvalidSource` without
  weakening Release domain validation.
- Apply cancellation after the operation report starts now stops the active Git
  process tree, waits for its exit, performs rollback and final-state capture,
  and completes the report with `OperationCanceled`.

## Current capability

An operator can safely preview or apply a documented Target Source without a
revision-like Release value resolving another commit. A canceled Apply is
available as a failed operation report rather than remaining in progress, and
the exclusive mutation guard is released only after cleanup completes.

## Deliberately deferred

- No Stage 15 capability was started.
- Platform topology, Bootstrap, deployment configuration, and service
  installation remain unchanged.

## Validation

- RustServer.Plugins Release build passed with warnings as errors.
- RustServer.Plugins tests passed: 30 total, including literal-Release,
  malformed-request, canceled-Apply rollback, report-finalization, guard, and
  Git-child shutdown regressions.

## Decisions

- `OperationCanceled` is the machine-readable Apply failure code when request
  cancellation occurs after the operation report has begun.
