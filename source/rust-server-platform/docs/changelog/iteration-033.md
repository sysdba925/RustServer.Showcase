# Iteration 033 — Standardize RustServer.Web Storybook coverage

## Goal

Make Storybook the backend-independent living catalog for the real
RustServer.Web UI, covering meaningful feature pages, panels, and operator
states without introducing a parallel demo application.

## Added and updated

- Added colocated stories for the Server, Overview, World, Plugins, app shell,
  and not-found UI, including ready, loading, unavailable, in-progress, error,
  empty, and confirmation states where the component supports them.
- Added deterministic typed control-plane and Plugins fixtures shared by
  stories and existing Overview tests.
- Added a Storybook-only HTTP boundary for direct API-bound Plugins pages. It
  returns typed published response data only for `/plugins-api` requests,
  delegates every other request, and restores the browser fetch function after
  each story.
- Loaded production global styles in Storybook, documented repository story
  conventions, and removed the generic Storybook onboarding examples and
  assets.
- Split the visual app shell from its provider composition so it can be
  documented without starting the Control Plane provider.

## Current capability

Developers can inspect the primary operator-facing UI states in Storybook with
no running Control, Versions, or Plugins backend. Story interactions use local
action callbacks, while API-bound Plugins pages use deterministic contract-shaped
responses.

## Deliberately deferred

- Stories do not substitute for backend integration or end-to-end mutation
  testing.
- Storybook does not expose API capabilities or workflow states absent from the
  published backend contracts.

## Validation

- `npm run lint` passed with existing warnings in `usePluginsResources.ts`.
- `npm run typecheck` passed.
- `npm run test` passed: 21 files and 170 tests.
- `npm run build` passed, with Vite's existing large-chunk advisory.
- `npm run build-storybook` passed.

## Decisions

- Stories remain beside production UI components and follow the feature
  hierarchy rather than a separate `src/stories` component tree.
- Fixture data uses the production TypeScript models; no Storybook-specific DTO
  interfaces were introduced.
- Plugins page stories mock only their existing raw HTTP boundary because those
  pages currently own direct API resource loading.
