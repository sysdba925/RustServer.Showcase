# Iteration 119 — Plugin catalog source management

## Goal

Allow operators to manage sources on catalog plugin details using the published PluginCatalog API.

## Added and updated

- Added source creation, editing and confirmed deletion with Site selection, optional label and HTTP(S) URL validation.
- Refresh the source list after changes and show errors for failed operations or refreshes.

## Current capability

Operators can select an existing Site by name or leave it unset, and manage links for an existing catalog plugin.

## Deliberately deferred

- Site, tag, folder and media management remain outside this change.

## Validation

- Web lint and typecheck passed; lint reported an existing CatalogPrototype warning.
- Focused source and details tests passed (6 tests).
- Web production build passed with a bundle-size warning.
- Catalog Development OpenAPI was cross-checked against the published contract.

## Decisions

- Sources retain optional Site IDs; the UI uses existing Sites and does not create them.
- PluginCatalog remains the owner of catalog metadata and source links.
