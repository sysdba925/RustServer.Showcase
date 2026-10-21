# Iteration 125 — Plugin Catalog ZIP restore in Web

## Goal

Allow operators to preview and restore a Catalog backup into an empty catalog.

## Added and updated

- Added a restore dialog with archive size validation, six collection counts, conflict details and separate commit confirmation.
- Send the original ZIP to the Catalog preview and commit endpoints; refresh the catalog and library filters after success.
- Block retries after an uncertain commit until the operator confirms checking the catalog and media storage.

## Current capability

Web can restore a backend-format ZIP into an empty catalog. Validation and emptiness are checked again by the backend during commit. HTTP validation, conflict, size, media type and server failures are surfaced to the operator.

## Deliberately deferred

- Replacement, merge, automatic cleanup, and release or deployment changes.

## Validation

- `npm run check` in RustServer.Web passed: lint, typecheck, 268 tests and production build. Existing lint and bundle-size warnings remain.
- Cross-checked restore request and response schemas against the available PluginCatalog Development OpenAPI and semantic contract.

## Decisions

- The backend owns ZIP validation and restoration. Web stores only a browser-side uncertainty marker, not catalog data or archive bytes, and requires explicit operator acknowledgement after a check.
