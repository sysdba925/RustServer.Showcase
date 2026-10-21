# Iteration 046 — Archive legacy Plugins Web UI

## Goal

Safely remove the superseded Plugins frontend from the active RustServer.Web
application while retaining it as reference material for a contract-aligned
replacement.

## Added and updated

- Moved the legacy Plugins pages, components, stories, fixtures, API client,
  API types, and client tests to `RustServer.Web/old/plugins-ui/`.
- Removed Plugins navigation, all legacy Plugins routes, active API exports,
  and the development proxy configuration that existed only for the legacy
  client.
- Removed the legacy diff-viewer dependency and updated shell and routing tests
  to verify that former Plugins URLs resolve to the existing Not Found page.

## Current capability

RustServer.Web now exposes only its implemented Overview, Server, and World
areas. The RustServer.Plugins backend remains available through its published
HTTP contract, but Plugins navigation and Web routes are temporarily
unavailable.

## Deliberately deferred

- A replacement Plugins Web implementation against the current
  RustServer.Plugins contract is deferred to the next task.
- No compatibility client, DTO adapter, temporary route, or placeholder UI is
  provided for the archived implementation.

## Validation

- RustServer.Web validation is recorded with the implementation change.
- The archived `old/plugins-ui/` tree remains outside TypeScript and Storybook
  source globs.

## Decisions

- Legacy Plugins Web code is reference-only because it targets a superseded
  backend contract and must not participate in the active runtime dependency
  graph.
- Platform Bootstrap requires no change: this is a Web source-organization and
  routing change, with no change to deployed services, topology values,
  executable paths, permissions, startup behavior, or readiness checks.
