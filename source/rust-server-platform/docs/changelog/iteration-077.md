# Iteration 077 — Auth and Dashboard public routing

## Goal

Route the existing Auth service and the administrator Web dashboard through the
production Nginx configuration without changing the Bootstrap, Updater, or
release-layout contracts.

## Added and updated

- Added explicit public Auth routing for login assets, portal endpoints, Auth
  APIs, and the unchanged Google and Steam callback paths. These routes retain
  their original URI when proxied to the loopback Auth listener.
- Reserved `/dashboard` for the static Web release and restricted the
  Dashboard, its assets, and the four existing backend API prefixes with the
  existing Auth `Admin`-role `auth_request` endpoint.
- Changed public root, `/index.html`, `/player`, and all unknown paths to
  return `404`; React SPA fallback is now limited to `/dashboard/*`.
- Preserved API prefix stripping, the loopback readiness listener, release
  identity header, Bootstrap validation/rollback, and release activation
  behavior.
- Documented the HTTPS/Secure-cookie/OAuth requirements and the pending
  RustServer.Web `/dashboard` build-base and router-basename work.

## Current capability

Bootstrap can install the same root-owned Nginx configuration idempotently.
When an administrator has a valid Auth session, Nginx can serve a compatible
Web release below `/dashboard` and proxy the existing administrative backend
APIs. No Node.js process is required in production.

## Deliberately deferred

- TLS certificates, public hostname selection, provider redirect-URI
  registration, and the HTTPS virtual-host design.
- RustServer.Web changes required to build assets and client-side routes below
  `/dashboard`.
- Any change to Auth, backend-service authorization, Updater, deployment
  executor, or release-directory structure.

## Validation

- `RUSTSERVER_BOOTSTRAP_TEST=test_staticweb_nginx_contract_is_root_owned_and_release_aware`
  `bash deploy/bootstrap/tests/bootstrap-tests.sh` passed, covering Auth URI
  preservation, OAuth callback routing, Dashboard asset/fallback boundaries,
  administrator authorization, API proxy invariants, and readiness.
- `bash -n` passed for the Nginx Bootstrap step and Bootstrap test script;
  `git diff --check` passed.
- `nginx -t` could not run locally because Nginx is not installed and the local
  Docker daemon is unavailable. No production host was modified.

## Decisions

- `/api/auth/authorize/admin` is the single Nginx authorization check for the
  administrator Dashboard and current backend API surface. This reuses Auth's
  existing role claim rather than adding a proxy-specific access model.
- Nginx must not rewrite a root-based Web build into `/dashboard`; the Web
  release itself owns its Vite base and React Router basename.
