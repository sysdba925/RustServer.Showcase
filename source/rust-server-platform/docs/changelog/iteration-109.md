# Iteration 109 — Plugin Catalog details, sources, and media

## Goal

Complete the Web operator workflows for one known PluginCatalog record while
preserving the boundary between catalog metadata and Rust plugin files,
deployment, and runtime state.

## Added and updated

- Added `/dashboard/plugins/catalog/:pluginId` with full Catalog metadata,
  Catalog-known version, record timestamps, selected icon, loading, missing,
  unavailable, and retry states.
- Added manual Source CRUD, local image/GIF upload, ordered media gallery,
  external Video/Preview links, caption/order edits, media deletion, and
  selected-icon set/clear actions through the published PluginCatalog API.
- Added explicit confirmations for Source, media, and Plugin deletion. Plugin
  deletion leaves the published `plugin_has_media` conflict visible rather than
  cascading media in the Web client.

## Current capability

When the Platform-provided Catalog route and its dependencies are available,
operators can manage the details, external sources, and media of a known
catalog entry. Local media uses the Catalog API prefix; external media remains
an external HTTP(S) link. The surface never presents Catalog data as installed,
deployed, or running plugin state.

## Deliberately deferred

- Platform Development topology/reverse proxy, PostgreSQL and media-root
  provisioning, Bootstrap/systemd/Nginx work remain Stage 08 scope.
- Authentication and authorization are absent from the Catalog API; upload and
  management endpoints are not production-public-ready.
- Rust runtime lifecycle, Git, deployment, releases, source scraping, and
  automatic version checks remain outside Catalog Web workflows.

## Validation

- RustServer.Web lint, TypeScript checking, and 240 unit tests passed locally.
- Catalog Development OpenAPI could not be cross-checked because
  `http://localhost:5180/openapi/v1.json` refused the connection. No Platform
  browser end-to-end or PostgreSQL/media-storage verification is claimed.

## Decisions

- Catalog CRUD remains in its own frontend API boundary and local feature
  state; no Catalog state is added to ControlPlaneProvider.
- Gallery order is rendered in the order returned by Catalog. Local original
  bytes are loaded only from an explicit view action; external links are not
  fetched, proxied, or embedded by Web.
- `ProblemDetails.code` remains untyped in Web until the Catalog Development
  OpenAPI publishes that extension. UI error presentation uses HTTP status,
  title, and detail.
