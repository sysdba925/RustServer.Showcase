# Iteration 128 — Plugin catalog card UX

## Goal

Align the catalog card workflow with the approved operator UX and the updated PluginCatalog contract.

## Added and updated

- Create and Edit share a compact form for catalog metadata, sources, library associations, date-only publication, segmented version and optional icon upload.
- Details presents read-only metadata, retains media management and provides confirmed deletion of the catalog entry and associated media.
- JSON import guidance and example use schema version 2; existing documents remain forwarded to the backend for validation.
- A partially saved card is reused on icon retry; an uncertain creation outcome requires checking the catalog before another attempt.

## Current capability

Operators can create and edit catalog cards with one core request, manage media, and delete a card with confirmation. Catalog tags are labels and do not establish runtime state.

## Deliberately deferred

- Binary media import and Rust server deployment or uninstall remain outside the catalog workflow.
- Automated tests were not changed or run at the user's request.

## Validation

- TypeScript production project check and production build passed. Test files are excluded from the production TypeScript project because their existing fixtures target the previous contract.
- Development OpenAPI was checked against the current Catalog checkout on a separate local port; the existing service on port 5180 was an older version.
- Visual viewport validation was not performed.

## Decisions

- PluginCatalog owns metadata, associations, date-only values and media deletion. Icon upload remains a separate Media API operation after the core save.
