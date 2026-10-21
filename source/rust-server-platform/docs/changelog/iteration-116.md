# Iteration 116 — Read-only Plugin Catalog browser

## Goal

Connect the Web catalog browser to the independent PluginCatalog API using the agreed prototype presentation.

## Added and updated

- Display paged catalog records in table and card layouts, including icons, source site names, tags, folders and prices.
- Send search and Site, Tag and Folder filters to the backend; provide loading, empty, error and retry states.
- Keep the prototype and Storybook on their existing mock data.

## Current capability

Operators can browse known catalog metadata; the page does not imply installation, deployment or runtime state.

## Deliberately deferred

- Editing, import, export and saved views are not offered by the catalog browser.

## Validation

- TypeScript typecheck and focused CatalogPage tests passed.
- Storybook production build passed with bundler warnings; lint passed with an existing prototype warning.
- Catalog Development OpenAPI was checked against the list and site schemas.

## Decisions

- Catalog filtering and pagination remain server-side, and site names are resolved from the Catalog sites list.
