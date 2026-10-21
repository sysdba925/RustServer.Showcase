# Iteration 052 — Plugins Management UI foundation

## Goal

Replace the temporary RustServer.Web Plugins placeholder with the approved V15-based management screen and connect its read-only Plugins state without exposing unfinished mutations.

## Added and updated

- Added the Plugins management screen in the shared Web shell with Environment and applied Source context, reserved command placement, and the Git Repositories and Applied Baseline panels.
- Added independent read-only Web integration for Plugins Environment, Baseline, Git status, Runtime Diff, and Releases, including local loading and unavailable presentation.
- Added deterministic Storybook coverage for Staging, Production, Git relations, Runtime changes, loading, and unavailable Plugins API states.
- Added the Web development proxy route for the existing Plugins topology address at `/plugins-api`.

## Current capability

Operators can open Plugins and inspect published Environment, applied baseline commits and time, local-to-known-remote Git relation, last fetch time, Runtime-to-Baseline summary, and known Release count. The screen remains usable when individual read resources are unavailable, without presenting simulated successful data.

## Deliberately deferred

- Environment switching, Fetch, Sync, Source selection and apply, Runtime commit/revert, dialogs, and History remain unavailable and visibly disabled.
- Runtime event-stream invalidation and mutation-operation reporting will be connected in later stages.

## Validation

- RustServer.Web `npm run check` passed with valid local topology values: lint, typecheck, 163 tests, and production build.
- RustServer.Web `npm run build-storybook` completed successfully without topology values or a running Plugins backend.
- RustServer.Plugins API contract verification completed successfully with `dotnet run --project tools/RustServer.Plugins.ApiContractGenerator -- --verify`.

## Decisions

- The UI keeps Environment, applied Baseline, Git state, and Runtime comparison separate; Git state is not treated as evidence that Runtime or Baseline equals a branch head.
- Existing Bootstrap already supplies and validates `RUSTSERVER_PLUGINS_URL`; adding the Web proxy requires no Bootstrap script, service, or topology-file change.
