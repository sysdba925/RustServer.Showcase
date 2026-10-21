# Iteration 121 — Manage catalog library

## Goal

Let operators maintain the catalog's sites, tags and folders from the catalog page.

## Added and updated

- Added a Manage library dialog with creation, editing and confirmed deletion of sites, tags and folders through PluginCatalog.
- Added input validation, API error display and pending-operation guards. Catalog data and filters reload after changes; a deleted active filter is cleared.

## Current capability

Operators can manage site names and optional base URLs, tag names and colors, and folder names. Deleting a site preserves its sources; deleting a tag or folder removes assignments without deleting plugins.

## Deliberately deferred

- Media management and backend changes are outside this iteration.

## Validation

- `npm run check` passed: lint, typecheck, 259 tests and production build. Lint reported an existing prototype warning; the build reported a bundle-size warning.
- PluginCatalog Development OpenAPI was cross-checked against the published contract.

## Decisions

- Library management uses the existing PluginCatalog API; it does not infer installation or runtime state.
