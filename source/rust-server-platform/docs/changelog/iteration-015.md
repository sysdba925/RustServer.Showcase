# Iteration 015 — RustServer.Web Prototype #1 Overview

## Goal

Implement the selected Prototype #1 application shell and a read-only Overview
presentation in the separate `RustServer.Web` repository, without introducing
backend integration or lifecycle/update mutations.

## Added and updated

- `RustServer.Versions` now publishes a committed OpenAPI contract at
  `openapi.json`, exposes the same generated contract through
  `GET /openapi.json`, and provides the generated human-readable
  `docs/api-contract.md` companion for frontend integration.
- The Versions API now verifies in tests that its committed OpenAPI and Markdown
  artifacts match the running ASP.NET implementation; a repository-local .NET
  generator updates or verifies both artifacts.
- `RustServer.Control` now publishes and verifies the same pair of generated
  API-contract artifacts for its production Control API, including a runtime
  `GET /openapi.json` endpoint for frontend consumers.
- `AppLayout` now provides the persistent semantic application chrome: product
  header, desktop sidebar, primary navigation, and a routed main-content area
  containing the React Router `Outlet`.
- The header displays `RUST SERVER PLATFORM` only. It does not present an
  aggregate connectivity, backend-health, or fabricated refresh indicator.
- The sidebar exposes only the implemented `Overview` route at `/` through an
  active `NavLink`; no planned or disabled destinations are displayed.
- The Overview page now establishes the Prototype #1 reading order: a primary
  Server region, equal Rust and Oxide Software regions, and an Activity region
  below them.
- Server, software, and activity regions use intentionally neutral structural
  text only. They do not report fabricated server state, versions, operations,
  or timestamps.
- CSS Modules define the application-shell and Overview layouts. Global CSS is
  limited to baseline rules and a small shared set of colour, spacing, and
  shell-size custom properties. The software cards stack at narrower widths.
- Route definitions are exported for isolated memory-router tests, while the
  application continues to use its browser router.
- Tests now cover the Overview route and shell identity, active Overview
  navigation, Server/Software/Activity regions, and the not-found route.
- `package.json` and its lockfile now declare Node.js `>=22.12.0`; the
  `RustServer.Web` README documents the same prerequisite.
- The Overview feature now owns typed `ResourceState` inputs, source response
  types, pure server-state and Oxide restart-required derivations, and
  deterministic development scenarios.
- `OverviewPage` explicitly uses `overviewScenarios.ready` as its temporary
  complete input and passes only the required typed resources to Server,
  Software, and Activity panels.
- Server presentation now renders local loading, unavailable, inconsistent,
  stopped, starting, ready, and active-update Maintenance states while retaining
  factual Running and Ready values.
- Equal Rust and Oxide cards now render their independent resource states,
  installed/running/available information, Rust runtime metadata, and the
  applicable update-available or restart-required indicators.
- Activity now presents a known operation or its local loading, unavailable,
  and no-known-operation states without claiming operation history, progress,
  cancellation, or live output.
- Focused React Testing Library coverage now verifies the Overview-panel
  presentation behavior, including independent unavailable resources and failed
  operation details.

## Current capability

The frontend route at `/` presents the deterministic ready Overview scenario:
the server runtime state, Rust and Oxide version information, and current
operation context are rendered from typed mock data. The mock source remains
obvious at the `OverviewPage` boundary and can be replaced later by independent
API resource loading.

## Deliberately deferred

- Control or Versions API clients, HTTP requests, MSW handlers, polling, React
  Context, global state, and independently loaded API resources.
- Lifecycle and Rust/Oxide update mutations, action buttons, operation polling,
  operation history, progress, stages, cancellation, and live output.
- Aggregate backend-health presentation, fabricated server metadata, uptime,
  start time, or operation duration.
- Additional navigation destinations, mobile-navigation JavaScript, Docker,
  deployment, and CI.

## Validation

- `git diff --check` passed for the frontend changes.
- Package manifests were parsed successfully and the Node engine requirement
  was verified in both `package.json` and `package-lock.json`.
- Targeted source scans found no deferred API, mock, domain-model, or fake
  connectivity/status implementation.
- `npm run check` passed with Node.js 22.14.0: Oxlint, TypeScript typecheck,
  29 Vitest tests, and the Vite production build all completed successfully.

## Decisions

- The existing UI documentation in `rust-server-platform/docs/ui` remains the
  authoritative product contract; the Overview remains a read-only
  presentation until public API resource loading is introduced.
- `RustServer.Web` remains a sibling repository. No frontend source was added
  to `rust-server-platform`; its contribution there is limited to changelog
  history and its authoring template.
- Deterministic typed mocks stay at the `OverviewPage` boundary so child panels
  remain presentational and can later receive independently loaded resources.
