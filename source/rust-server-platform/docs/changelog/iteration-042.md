# Iteration 042 — Runtime Revert and mutation diagnostic recovery

## Goal

Complete Stages 17–18 for RustServer.Plugins: restore managed Runtime files
from the persistent Baseline and make failed mutating operations retain useful
final-state diagnostics without changing deployment topology.

## Added and updated

- Stage 17 added `POST /api/runtime/revert`, which restores the managed Runtime
  set from the exact Plugins and Configs commits persisted in Baseline. The
  operation is binary safe, does not fetch or change Git workspaces, and rolls
  Runtime back to its command-scoped pre-operation snapshot when restoration
  fails or is cancelled.
- Stage 18 hardened Fetch, Apply, Commit + Push, and Revert reporting so
  cancellation completes cleanup and final-state capture before releasing the
  mutation guard; known unavailable workspace, remote, and push outcomes keep
  their specific diagnostics; no Git auto-repair was introduced.
- The Stage 18 correction preserves the exact active Fetch report phase,
  including cancellation during local Development updates or final-state
  capture. It also classifies unreadable Git source-head resolution during
  Apply as `GitRepositoryUnavailable`, distinct from a readable but absent
  exact target ref (`TargetSourceUnavailable`).

## Current capability

In both Environments, operators can restore managed Runtime changes to the
persisted applied Baseline without substituting current Git HEAD or changing a
workspace. Failed mutations provide a finalized, phase-accurate report while
the exclusive guard remains held through cleanup and final-state capture.

## Deliberately deferred

- No new deployment configuration, service endpoint, topology key, branch
  type, Bootstrap action, or architecture boundary was introduced.
- Operation reports remain intentionally process-local; after a crash or
  restart, recovery is based on observed Git Status, Baseline, Runtime Diff,
  and Environment/SystemState rather than inferred unfinished work.

## Validation

- RustServer.Plugins Release build passed with warnings as errors.
- RustServer.Plugins full Release test suite passed: 40 tests, including
  binary-safe Runtime Revert, Runtime rollback on failure, unavailable Git
  source validation, Fetch cancellation phase/report finalization, guard
  release, and unsafe dirty workspace regressions.

## Decisions

- Runtime Revert is strictly `Baseline -> Runtime`: it does not substitute
  Remote Git, local HEAD, or a branch for persisted Baseline commits.
- `GitSourceChangedSinceBaseline` means readable remote-tracking SHA mismatch;
  Git repository/CLI unavailability remains `GitRepositoryUnavailable`.
- `TargetSourceUnavailable` describes a readable, absent exact source ref; it
  does not mask local Git CLI, workspace, or repository failures.
