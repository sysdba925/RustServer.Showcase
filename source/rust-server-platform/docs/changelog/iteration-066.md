# Iteration 066 — Resilient Web release publishing

## Goal

Make `RustServer.Web` publishing clear to operate and safe to retry after a
DryRun or failed local attempt without weakening remote release protection.

## Added and updated

- The Web publisher now reports its Git validation, GitHub authentication,
  dependency, typecheck, test, build, packaging, checksum, release-creation,
  and retention stages as they run.
- Artifacts are built in a temporary directory and replace only the two
  calculated archive/checksum paths for the selected version, so stale local
  attempt output no longer blocks a retry or affects unrelated files.
- The final publisher and CMD-launcher output identifies a successful version
  and release URL, while failures have an explicit error marker.
- RustServer.Auth now has Development and Production GitHub publishing
  profiles for framework-dependent `net10.0` `linux-x64` releases. Publishing
  packages the complete output with a SHA-256 sidecar, verifies the
  synchronized `develop` branch and existing non-interactive GitHub CLI
  session, prevents tag and artifact reuse, and retains the two newest
  releases in each channel after successful publication.

## Current capability

An operator can repeat a DryRun for the same calculated Web version and retry
a failed publication without manually cleaning `artifacts/releases`. Remote
tag/release collision checks, version calculation, and successful-release
retention stay unchanged.

## Deliberately deferred

- No GitHub Release was created during validation.
- No change was made to the production deployment or release-activation
  workflow.

## Validation

- PowerShell syntax validation for `scripts/publish.ps1`.
- Repeated Development DryRuns with the same calculated `v0.1.0-dev.1` local
  artifact; the archive and sidecar were regenerated and SHA-256-verified.
- RustServer.Auth Release build and its 16 existing tests passed. Both publish
  profiles resolve to `Release`, `net10.0`, `linux-x64`, framework-dependent;
  the Development dry run correctly refused the intentionally dirty checkout
  before it could create a GitHub Release. A local Auth archive and checksum
  were generated and inspected against the Updater package contract.

## Decisions

- Local artifact replacement is limited to the exact versioned archive and
  checksum names after reparse-point and directory checks; remote version
  collision prevention remains the authority for release uniqueness.
