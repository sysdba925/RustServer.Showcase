# Iteration 041 — Release navigation and Runtime Commit + Push

## Goal

Complete the operator-visible Release navigation semantics and provide the
Staging-only Runtime-to-Git Commit + Push workflow for RustServer.Plugins.

## Added and updated

- `GET /api/releases` now marks the server's current Release from the
  persistent Baseline and independently reports whether known remote Release
  HEADs still exactly match the applied Baseline commits.
- Release rollback remains the existing Preview → Apply flow for any older
  complete Release; no separate rollback endpoint or workflow was added.
- `POST /api/runtime/commit` accepts a commit message and explicitly selected
  managed Runtime paths, creates one commit per affected Plugins/Configs
  repository, then pushes canonical `origin` without force.
- Commit + Push performs fresh fetch, requires remote Source heads to match
  Baseline SHA, stages only selected paths, preserves Baseline and Runtime,
  and reports partial push outcomes without attempting to rewrite an already
  pushed remote.

## Current capability

Operators can see which Release is actually applied to the server even when
the branch has since changed in Git. In Staging they can persist selected
Runtime changes to the applied Development or Release branch; Production
commits remain blocked.

## Deliberately deferred

- Runtime revert (Stage 17) is not implemented.
- No new deployment configuration, service endpoint, topology key, branch
  type, or Bootstrap action was introduced.

## Validation

- RustServer.Plugins Release build passed with warnings as errors.
- Focused integration tests passed for selected multi-repository commit and
  push, remote/Baseline mismatch rejection, partial push reporting, and
  forward/backward Release Apply semantics.
- RustServer.Plugins full Release test suite passed: 34 tests.

## Decisions

- `current` Release identity is Baseline-owned; local branch and Git HEAD do
  not substitute for the applied server state.
- `matchesBaseline=false` reports unequal known SHA values only; it does not
  claim ancestry or that a branch is newer.
- A successful Runtime Commit + Push intentionally leaves Git Source newer
  than the applied Baseline until a normal Apply.
