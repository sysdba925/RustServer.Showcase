# Iteration 115 — Plugin Catalog contract alignment

## Goal

Align the Web Catalog API boundary with the current PluginCatalog contract while retaining the existing catalog screens.

## Added and updated

- Updated plugin, source and media DTOs and requests, including Site IDs and Technical media.
- Added Sites, Tags and Folders operations, plugin associations and catalog filters.
- Adjusted existing metadata and source forms to use supported requests and preserve other metadata.

## Current capability

Web can manage catalog plugins, sources and media, and the API client exposes catalog classifications and filters.

## Deliberately deferred

- JSON import, ZIP export and restore, and new screens.

## Validation

- Web lint, typecheck, tests and build run through npm run check.
- Development OpenAPI was unavailable at localhost:5180; cross-check remains outstanding.

## Decisions

- Catalog metadata is independent of installed or running plugin state.
