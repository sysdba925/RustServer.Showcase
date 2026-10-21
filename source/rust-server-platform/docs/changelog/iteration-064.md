# Iteration 064 — Production StaticWeb Nginx bootstrap

## Goal

Provide the platform-managed production Nginx boundary required to serve a
verified `RustServer.Web` StaticWeb release through the existing Updater.

## Added and updated

- Production Bootstrap now installs and reconciles a root-owned Nginx
  configuration for `/opt/rustserver/web/current`, including React SPA routing,
  static-asset cache policy, and same-origin API routes that match Vite's
  prefix-stripping development proxies.
- A loopback-only readiness origin at `127.0.0.1:8081` returns `200` only for
  a resolved, versioned release with `index.html`, and emits one
  `X-RustServer-Release` header derived from the `current` symlink target.
- The Bootstrap-owned initial application-registry template now declares the
  `RustServer.Web` `StaticWeb` registration. Existing installed registries
  remain create-only and are not altered.
- Bootstrap creates the root-owned Web release layout, validates Nginx before
  configuration installation, and reloads it only when its managed
  configuration changes.

## Current capability

The Deployment Executor can prove a Web release through Nginx after it
atomically changes `current`; it does not need to reload or restart Nginx for
install, update, rollback, or recovery. API requests are proxied only to
loopback backend addresses and cannot fall through to the SPA.

## Deliberately deferred

- Building and publishing `RustServer.Web` release artifacts.
- A reviewed migration that adds the Web registration to an already installed
  root-owned `applications.ini`.
- TLS termination, certificate lifecycle, and external firewall policy.

## Validation

- Extended the existing Bootstrap test script with the Nginx/static-registration
  contract assertions.
- Runtime Nginx routing validation remains to be run in an isolated Ubuntu
  environment; no production server was modified.

## Decisions

- The readiness endpoint uses a distinct loopback listener rather than public
  SPA content, preventing a default virtual host or fallback page from
  satisfying release readiness.
- Nginx derives the release marker from `$realpath_root` on every probe so the
  marker follows atomic symlink activation without mutable deployment state.
