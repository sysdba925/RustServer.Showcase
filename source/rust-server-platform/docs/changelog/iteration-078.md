# Iteration 078 — Dashboard subpath hosting

## Goal

Prepare the RustServer Web control panel for same-origin production hosting
under `/dashboard` without changing backend APIs, Nginx, or deployment flows.

## Added and updated

- Configured Vite to publish the Web application's static resources below
  `/dashboard/assets/`.
- Configured the React browser router with the `/dashboard` basename, including
  navigation for Overview, Server, World, and Plugins.
- Added routing coverage for direct dashboard URLs and dashboard-prefixed
  navigation links.
- Documented the dashboard path and its separation from the root API prefixes
  in RustServer.Web's README and publishing guide.

## Current capability

The prebuilt Web application can be served as the `/dashboard` SPA. Its assets
and internal navigation remain under that path, while requests to
`/control-api`, `/versions-api`, `/plugins-api`, and `/snapshots-api` retain
their existing origin-root routes.

## Deliberately deferred

- Nginx configuration, compatibility rewrites for root Web URLs, and any
  backend or Auth changes remain outside this iteration.
- Deployment and release publication are unchanged and were not performed.

## Validation

- `npm test` — passed: 25 test files and 198 tests.
- `npm run lint` — passed.
- `npm run build` — passed.
- Inspected `dist/index.html`: generated JavaScript and stylesheet URLs begin
  with `/dashboard/assets/`; no dashboard-prefixed API path was emitted.

## Decisions

- Vite's `/dashboard/` base governs only static application resources.
- React Router's `/dashboard` basename governs only SPA routes. The four
  backend API prefixes remain root-relative so the existing Nginx API proxies
  continue to receive the same URLs.
