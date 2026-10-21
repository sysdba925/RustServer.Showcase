# Iteration 124 — Plugin Catalog ZIP export in Web

## Goal

Allow operators to download a complete Plugin Catalog backup from the Web catalog.

## Added and updated

- Added an export dialog with a loading state, retryable errors, and duplicate-request protection.
- Download the backend-generated archive as `plugin-catalog.zip`, independently of catalog filters and pagination.

## Current capability

The catalog can request a complete ZIP export through the configured PluginCatalog API. A 503 is surfaced when the export is unavailable, including when a media original cannot be read.

## Deliberately deferred

- ZIP restore and release or deployment changes.

## Validation

- `npm run check` in RustServer.Web: lint, typecheck, 265 tests and production build passed. Lint and build reported existing warnings.
- Cross-checked `GET /api/export` and its 503 response against the available PluginCatalog Development OpenAPI and semantic contract.

## Decisions

- The backend creates the archive; Web does not construct ZIP files or derive export contents from the current list.
