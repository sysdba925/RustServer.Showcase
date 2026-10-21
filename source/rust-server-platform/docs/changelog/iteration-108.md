# Iteration 108 — Plugin Catalog web surface

## Goal

Add a Catalog page to the existing RustServer.Web Plugins surface for operator
management of known plugin records without extending Git, deployment, Rust
lifecycle, or Platform infrastructure responsibilities.

## Added and updated

- Added `/dashboard/plugins/catalog` with backend-paged catalog entries,
  debounced search, source-kind filtering, selected-icon thumbnails, loading,
  empty, retry, and local unavailable states.
- Added Catalog metadata create and edit dialogs for the published PluginCatalog
  request fields, plus local navigation between Plugins Management and Catalog.
- Added an independent same-origin browser prefix, `/plugin-catalog-api`,
  pinned in production build configuration.

## Current capability

Operators can browse and maintain known plugin catalog metadata through the
Catalog API when its Platform route is available. Catalog presentation clearly
does not imply that an entry is installed, deployed, or running on Rust.

## Deliberately deferred

- Plugin details, source editing, media galleries, uploads, icon selection,
  media deletion, and catalog-plugin deletion remain Stage 07 work.
- Development topology, reverse proxy, Nginx, systemd, PostgreSQL, media-root,
  and Bootstrap integration remain Stage 08 work.

## Validation

- RustServer.Web lint, TypeScript, unit tests, and production build are run as
  part of the Stage 06 change verification.
- The Catalog Development OpenAPI and liveness endpoint were inspected locally;
  no end-to-end browser integration through Platform topology was claimed.

## Decisions

- PluginCatalog has an explicit frontend API boundary and same-origin prefix;
  it does not reuse the RustServer.Plugins client or acquire deployment rights.
- Selected icon thumbnails are resolved only through the Catalog API prefix.
- Catalog operations preserve backend search, source filtering, ordering, and
  pagination rather than simulating global filtering in the browser.
