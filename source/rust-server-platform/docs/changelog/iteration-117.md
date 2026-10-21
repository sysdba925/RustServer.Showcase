# Iteration 117 — Read-only Plugin Catalog details

## Goal

Connect Plugin Details to the independent Catalog API using the agreed prototype presentation.

## Added and updated

- Open details from plugin names and icons in table and card layouts.
- Show catalog metadata, named source sites, tags, folders, prices and media, including Markdown source descriptions.
- Provide loading, not-found, error and retry states for detail requests.

## Current capability

Operators can inspect known catalog records and follow their source and media links. Local media URLs are resolved through the Catalog API prefix.

## Deliberately deferred

- Creating, editing, deleting and uploading remain unavailable on this page.

## Validation

- TypeScript typecheck and ten focused catalog and details tests passed.
- Lint passed with an existing prototype warning. Catalog Development OpenAPI was checked against the details, media and source schemas.

## Decisions

- Catalog metadata describes known records, not installed, deployed or running plugins. Storybook retains its existing mock prototype.
