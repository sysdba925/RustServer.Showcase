# Storybook

Storybook is RustServer.Web's living UI catalog. It documents the real visual
states of the application without requiring a running backend.

- Place a `*.stories.tsx` file beside the page, panel, or reusable visual
  component it documents. Do not build a parallel demo component tree.
- Use titles that mirror feature architecture, such as `Server/Panels/Runtime`
  and `Plugins/Incoming Changes`.
- Story data must use the production domain and API types. Never create a
  Storybook-only DTO shape.
- Put deterministic domain states reused by stories or tests in the feature's
  `fixtures/` directory. Fixtures must be side-effect free and independent of
  Storybook APIs.
- Prefer component stories. Add page stories when a real provider or router
  decorator can render the page without backend access. Keep API-bound
  containers out of stories until they have a suitable application boundary.
- Use Storybook callback helpers for actions. Stories must never call a real
  backend.

Run the catalog with `npm run storybook`. For CI and catalog validation, use
`npm run build-storybook`; it compiles every configured story into the static
catalog.
