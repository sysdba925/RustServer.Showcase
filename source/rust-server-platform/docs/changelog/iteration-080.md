# Iteration 080 — Channel-aware initial Updater installation

## Goal

Make a clean Production installation select the highest published compatible
`RustServer.Updater` release for the operator's chosen release channel, without
changing the explicit emergency upgrade workflow.

## Added and updated

- Added a channel-selection step before the initial Updater archive download.
  Development accepts only non-draft `vX.Y.Z-dev.N` prereleases and Production
  only non-draft stable `vX.Y.Z` releases.
- Added authenticated, paginated GitHub Releases API discovery for the private
  `sysdba925/RustServer.Updater` repository. Candidate versions are compared as
  SemVer numeric components rather than by publication time.
- Stored the channel and exact tag selected by step 45 in root-owned volatile
  state, so separately launched steps 50 and 55 cannot reselect a different
  release. Step 50 consumes and removes that state only after activation.
- Removed the clean-install dependency on the fixed `v0.1.0-dev.1` Bootstrap
  default. The archive and SHA-256 asset names are now derived from the selected
  published tag and retain the existing metadata, checksum, archive-path, and
  extraction checks.
- Reject malformed GitHub release metadata, draft releases, missing matching
  releases, unsafe asset URLs, and mismatched tag metadata with explicit errors.
- Preserved the active Updater on a repeated `install`; it does not query GitHub
  or implicitly update `current`. `upgrade-updater <release-tag>` remains the
  only explicit, transactional self-update path and retains its rollback.
- Updated operator documentation with the clean-install ordering, channel
  filters, non-interactive input, and no-fallback behavior.

## Current capability

On a new host, an operator chooses a release channel before any Updater archive
is fetched. Bootstrap verifies that the selected channel contains a suitable
published release, persists the choice, and installs that exact verified
release. A channel with no match fails clearly and is not silently changed.

## Deliberately deferred

- Automatic updates of an already running Updater remain out of scope.
- Development Bootstrap, Web, Auth, and backend services are unchanged.
- No deployment or production-host operation was performed.

## Validation

- Added focused Bootstrap tests for channel selection, Development/Production
  filtering, SemVer ordering, no matching release, malformed GitHub metadata,
  transfer across separate step processes, and repeated-install preservation.
- `bash -n` and `git diff --check` were run for the changed installer scripts.

## Decisions

- The channel is persisted only after the Releases API proves that it has a
  suitable non-draft release. This makes a failed first attempt retryable while
  keeping existing persisted selections authoritative.
- Explicit `upgrade-updater` tags are not channel-selected; they retain their
  reviewed transactional update and rollback contract.
