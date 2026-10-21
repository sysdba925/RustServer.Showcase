# Iteration 079 — Development dashboard readiness

## Goal

Align Development Bootstrap with the Web application's established
`/dashboard` path so Development and Production expose one dashboard address.

## Added and updated

- Changed Development Web readiness to request
  `RUSTSERVER_WEB_URL/dashboard/` and accept only a direct HTTP `200`; an HTTP
  redirect no longer satisfies readiness.
- Added a focused Bootstrap test for the derived dashboard URL and rejection of
  a `302` readiness response.
- Documented the Development dashboard URL and direct-readiness rule.

## Current capability

Development Bootstrap starts Vite and verifies that the dashboard entry point
is actually available at `/dashboard/`. The browser application, static assets,
and client routes remain below `/dashboard`, while `/control-api`,
`/versions-api`, `/plugins-api`, and `/snapshots-api` remain origin-root paths.

## Deliberately deferred

- Production Nginx, deployment, backend API contracts, and routing ownership
  remain unchanged.
- No deployment or production-host validation was performed.

## Validation

- `npm run check` — passed: lint, TypeScript, 26 Vitest files / 203 tests, and
  production build.
- `bash -n scripts/bootstrap.d/92-build-rustserver-web.sh
  scripts/bootstrap.d/94-install-rustserver-web-service.sh
  scripts/tests/rustserver-web-bootstrap-test.sh` — passed.
- `bash scripts/tests/rustserver-web-bootstrap-test.sh` — passed.
- A local topology-configured Vite HTTP probe passed: `/dashboard/`,
  `/dashboard/server`, `/dashboard/world`, and `/dashboard/plugins` returned
  `200`; `/` returned `302`; all four root API proxy prefixes forwarded
  requests to their corresponding upstream after removing the public prefix.

## Decisions

- A redirect from the Vite origin root is not dashboard readiness. The probe
  must request the final shared application address and receive `200` there.
- Vite's static base and React Router's basename stay scoped to `/dashboard`;
  development proxy routes remain root-relative.
