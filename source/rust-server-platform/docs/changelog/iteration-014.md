# Iteration 014 — RustServer.Web frontend bootstrap

## Goal

Create the initial technical foundation for the separate `RustServer.Web`
repository. This iteration establishes the frontend application boundary only;
it does not implement Prototype #1 or integrate with backend services.

## Added

- A separate Git repository at `E:\Projects\rust\RustServer.Web`, with no
  commit created.
- A React, TypeScript, and Vite application at the repository root, including
  its generated `package-lock.json` and project-specific `.gitignore`.
- Explicit React Router setup with the Overview route at `/` and a minimal
  not-found route.
- A minimal `AppLayout` route boundary, without sidebar, navigation, or the
  Prototype #1 visual shell.
- A feature-oriented source structure containing `app`, `features/overview`,
  `shared/config`, and `shared/api` boundaries.
- Typed frontend environment configuration for
  `VITE_CONTROL_API_BASE_URL` and `VITE_VERSIONS_API_BASE_URL`, with no
  default development VM or localhost URL.
- A CSS reset/baseline only; no CSS framework, design-token system, or product
  palette was introduced.
- Vitest, jsdom, and React Testing Library, including one smoke test proving
  that the Overview route renders.
- `dev`, `build`, `lint`, `typecheck`, `test`, and combined `check` npm
  scripts.
- Repository README describing purpose, stack, local configuration, related
  backend components, the UI-contract documentation location, and deployment
  responsibility.

## Deliberately deferred

- Prototype #1 UI, sidebar/navigation, Server/Rust/Oxide cards, and activity
  presentation.
- Control and Versions API clients, network requests, domain types, polling,
  mutations, fixtures, and derived server or update state.
- Docker, deployment/orchestration integration, CI, and frontend commits.

## Validation

- `npm install` completed and generated `package-lock.json`.
- `npm run lint` passed.
- `npm run typecheck` passed.
- `npm test` passed: 1 test file and 1 test.
- `npm run build` passed.
- `npm run check` passed, executing lint, typecheck, tests, and production
  build in sequence.

## Decisions

- `RustServer.Web` remains a sibling repository and contains no source code
  inside `rust-server-platform`.
- No real backend request exists in the frontend bootstrap.
- The product/UI contract remains authoritative in
  `rust-server-platform/docs/ui`; it was referenced, not duplicated.
