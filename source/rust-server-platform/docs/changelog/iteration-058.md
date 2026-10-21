# Iteration 058 — Plugins release and Runtime Diff review improvements

## Goal

Make the existing V15 Plugins workflows safer and easier to inspect without
changing the RustServer.Plugins HTTP contract or any Runtime/Git state.

## Added and updated

- RustServer.Web Switch Source now shows complete and incomplete Releases in a
  fixed-height, scrollable list. Each entry identifies the Plugins and Configs
  branch presence; incomplete Releases state why they are unavailable and
  remain disabled.
- The Applied Baseline panel now has a Runtime Diff Refresh action. It rereads
  only the published Runtime Diff resource and shows loading or request-failure
  state without fetching Git, reloading the page, or changing Runtime files.
- Runtime Diff content now uses aligned Baseline and Runtime columns. Text
  rows retain neutral, removed-red, and added-green presentation; missing and
  binary sides remain explicitly represented.
- RustServer.Versions now has Development and Production GitHub publishing
  profiles for framework-dependent `linux-x64` releases. Publishing verifies
  the synchronized `develop` branch, produces checksummed release archives,
  prevents tag/artifact reuse, and retains the two newest releases per
  channel after a successful GitHub release is created.
- RustServer.Versions publishing now resolves the remote `develop` SHA through
  authenticated GitHub CLI API access instead of a network Git command, so
  Visual Studio publishing does not invoke Git Credential Manager.

## Current capability

Operators can diagnose an incomplete Release before attempting to switch
source, refresh their current Runtime-to-Baseline comparison on demand, and
compare changed text in aligned columns. Added, modified, deleted, missing,
and binary content remain distinguished using the existing read-only Plugins
API.

## Deliberately deferred

- No backend endpoint, OpenAPI artifact, Git Fetch, or Runtime mutation was
  introduced; refresh relies on the existing `GET /api/runtime/diff`.
- Release repair remains an external Git operation followed by Fetch.

## Validation

- RustServer.Web `npm run check` passed: lint, typecheck, 197 tests, and
  production build.
- RustServer.Web `npm run build-storybook` passed.
- Bootstrap scripts for RustServer.Web were reviewed; no bootstrap change is
  required because deployment topology and service inputs are unchanged.

## Decisions

- The nullable repository heads published by the release catalog are rendered
  only as branch-availability diagnostics; the client does not infer or repair
  Git state.
- Runtime Diff refresh remains a read-only, independently refreshable resource
  rather than a general dashboard reload.
