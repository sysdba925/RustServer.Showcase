# Iteration 050 — Topology-free Storybook startup

## Goal

Allow RustServer.Web Storybook to run locally without RustServer services or
topology environment variables, while preserving strict topology validation for
the application development server and production build.

## Added and updated

- Configured the Storybook Vite builder to load a dedicated, topology-free
  Storybook Vite config rather than the application `vite.config.ts`.
- Kept the application Vite config as the sole owner of topology validation,
  proxy targets, and development-server binding configuration.
- Included the Storybook Vite config in the Node TypeScript project.

## Current capability

Developers can run `npm run storybook` and `npm run build-storybook` without
starting backend services or exporting topology variables. `npm run dev` still
requires valid topology configuration before it starts.

## Deliberately deferred

- Storybook continues to use component fixtures and does not make backend
  service requests.
- No backend, topology, Bootstrap, or application proxy capability changed.

## Validation

- `npm run storybook` reached Storybook ready at `http://localhost:6006/`
  without topology environment variables.
- `npm run build-storybook` completed successfully without topology variables.
- `npm run check` passed: lint, typecheck, 155 tests, and production build.
- Confirmed `npm run dev` still rejects a missing `RUSTSERVER_CONTROL_URL`.

## Decisions

- Storybook explicitly selects its own Vite config through the supported
  Storybook builder option, rather than weakening or conditionally bypassing
  application topology validation.
- Bootstrap requires no change because its Web step already provides topology
  for application validation; Storybook is a local development tool and is not
  part of the deployed service workflow.
