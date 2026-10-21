# Iteration 049 — RustServer.Web foundation shell

## Goal

Establish the shared RustServer.Web application shell from the approved Plugins
V15 visual direction while keeping Plugins functionality and backend
integration explicitly out of scope.

## Added and updated

- Modernized the single React application shell with the V15-inspired Rust
  header, permanent Overview / Server / World / Plugins navigation, active
  route treatment, routed content area, and room for a future footer.
- Introduced shared Web design tokens plus small reusable Panel and Badge
  primitives, each with Storybook coverage.
- Restored `/plugins` as a minimal Stage 1 placeholder in its own feature
  boundary. It makes no backend requests and exposes no Plugins operations.
- Extended route tests for the Plugins placeholder, navigation, active routes,
  and preservation of the existing Overview, Server, and World areas.

## Current capability

Operators can navigate between Overview, Server, World, and a clearly scoped
Plugins foundation placeholder within one consistent application shell. The
existing operational pages retain their current behavior.

## Deliberately deferred

- Plugins API integration, Git workflows, Runtime actions, History, and all
  deployment or release functionality remain unimplemented.
- Existing Overview, Server, and World internals are not redesigned in this
  shell-foundation iteration.

## Validation

- RustServer.Web `npm run check` passed: lint, typecheck, 155 tests, and
  production build.
- RustServer.Web `npm run build-storybook` completed successfully.

## Decisions

- Plugins remains a dedicated Web feature boundary without adding
  Plugins-specific state to the shared Control Plane provider.
- Bootstrap requires no change: the Web build, service, topology, paths,
  permissions, readiness behavior, and deployed capabilities are unchanged.
