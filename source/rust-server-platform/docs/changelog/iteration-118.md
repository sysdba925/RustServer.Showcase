# Iteration 118 — Catalog plugin metadata editing

## Goal

Connect plugin creation and metadata editing in Web to the PluginCatalog API.

## Added and updated

- Added catalog creation and detail editing with Markdown preview, validation, API errors and duplicate-save protection.
- Preserved catalog-known version when updating metadata and retained the full last-published timestamp unless edited.

## Current capability

Operators can create a catalog entry, open its details and edit its supported metadata. Price and currency are entered together.

## Deliberately deferred

- Sources, tags, folders, media and plugin deletion management.

## Validation

- Web `npm run check` passed (253 tests and production build); lint and build reported existing warnings.
- PluginCatalog Development OpenAPI was checked against its published contract.

## Decisions

- PluginCatalog remains the owner of catalog metadata; editing does not imply deployment or runtime state.
