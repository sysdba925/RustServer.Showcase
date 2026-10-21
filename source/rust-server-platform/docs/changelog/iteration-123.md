# Iteration 123 — Catalog JSON import

## Goal

Connect the catalog JSON import workflow to PluginCatalog while preserving the prototype presentation.

## Added and updated

- Added file selection, server preview, validation errors, duplicate and missing library item display, and explicit confirmation before commit.
- Show created and skipped counts and refresh catalog data after a successful import. Prevent repeated submissions and require operator inspection when the commit outcome is uncertain.

## Current capability

Operators can preview and import a JSON collection using the PluginCatalog API, explicitly permitting creation of missing Sites, Tags and Folders when needed.

## Deliberately deferred

- ZIP export and restore remain outside this change.

## Validation

- Development OpenAPI was checked against the published Catalog contract.
- Focused import and catalog page tests passed (8 tests); TypeScript typecheck, lint and production build passed. Lint reported an existing prototype warning; build reported a bundle-size warning.

## Decisions

- PluginCatalog validates the original JSON document and owns duplicate detection and commit semantics. The runtime does not use prototype storage.
