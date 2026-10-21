# Iteration 094 — Restore official Rust branding across Dashboard and Auth

## Goal

Restore the two official Rust icon placements used by the Overview V5 prototype:
the browser favicon and the small Rust logo inside the shared top-left header
tile. Ensure Auth's Login and Admin Portal documents display the same favicon
rather than the browser's generic missing-icon indicator.

## Added and updated

- `RustServer.Web/index.html` now declares the official Rust site icon,
  `https://rust.facepunch.com/favicon.ico`, with an explicit
  `<link rel="icon" type="image/x-icon">`. The single HTML entrypoint
  supplies the icon on Overview, Server, World and Plugins routes.
- `RustServer.Web/src/app/layout/AppLayout.tsx` uses that same official Rust
  image in the shared header tile instead of the hard-coded letter `R`.
  The tile's existing styling remains; the `R` is displayed only when the
  external image fails to load.
- `RustServer.Auth/wwwroot/index.html` (Login) and
  `RustServer.Auth/Views/Portal/Admin.cshtml` (Admin Portal) now declare
  the same favicon in their HTML heads. Both Auth headers already use the
  official Rust logo and retain their existing on-error fallback.
- Added a Web application-shell regression test for image source/fallback and
  Auth integration checks for favicon markup on Login and rendered Admin Portal.

## Current capability

All Dashboard routes share a single favicon and consistent top-left Rust tile.
Auth's independent HTML pages use the same favicon. These links refer directly
to the official Rust host as in the UX prototype; no new local asset or
backend endpoint is required.

## Deliberately deferred

- Icons remain subject to the availability of the external official Rust
  hostname. If the browser cannot load it, the Dashboard/Auth header uses
  the existing `R` fallback; browser favicon rendering is browser-dependent.
- No Nginx, Bootstrap, Updater, API contract, authentication flow or
  additional frontend-page changes were made. Auth and Web must each be
  published/deployed for their respective HTML changes to reach the server.

## Validation

- Inspected the Overview V5 prototype and shared shell's icon source and
  compared it with Web's shared header and both Auth HTML heads.
- Verified the updated GitHub `develop` files and added tests covering
  Web logo/fallback and Auth icon markup.
- **Pending:** `npm run check` in `RustServer.Web`, `dotnet test` in
  `RustServer.Auth`, and a browser smoke test on deployed Dashboard,
  `/login`, and `/admin`. These commands were not run in the
  GitHub-backed editing environment.

## Decisions

- Use the same externally hosted icon URL as the chosen prototype across
  both independently deployed UIs.
- Keep the logo within `AppLayoutFrame` so all Dashboard routes inherit
  it automatically; declare favicons in each HTML document, not per React route.
