# Iteration 051 — Full-screen application Storybook previews

## Goal

Make complete RustServer.Web application screens available for visual review in
Storybook without requiring RustServer services or changing runtime behavior.

## Added and updated

- Added `Pages / Full Screen` Storybook previews for Overview, Server, World,
  and the current Plugins placeholder.
- Each preview composes the real `AppLayoutFrame`, its Header and Sidebar, the
  corresponding page component, router-backed active navigation state, and the
  existing side-effect-free Control Plane fixture where page data is required.
- The World preview also intercepts its independently loaded snapshot-list
  request with an empty-list Storybook fixture.
- Configured the previews for Storybook's full-screen canvas layout so they are
  practical to review at desktop dimensions.

## Current capability

Developers can select any `Pages / Full Screen` story and visually review the
complete application shell and page content without running backend services.

## Deliberately deferred

- Plugins remains the existing Stage 1 placeholder; no Plugins workflows or
  service integration are introduced.
- The application router, layout, API clients, and backend behavior remain
  unchanged.

## Validation

- RustServer.Web `npm run check` passed with valid local topology values:
  lint, typecheck, 155 tests, and the production build.
- RustServer.Web `npm run build-storybook` completed successfully without
  topology variables or backend services.

## Decisions

- Full-screen stories supply only Storybook-local router and fixture context;
  they reuse the production application frame and pages rather than creating a
  parallel shell implementation.
- Bootstrap requires no change: these are local visual-review artifacts and do
  not affect the deployed Web build, topology, service startup, or readiness.
