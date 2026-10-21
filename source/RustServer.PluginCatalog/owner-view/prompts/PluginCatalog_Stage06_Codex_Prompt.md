# Codex — RustServer.PluginCatalog / Stage 06 of 09: Web Catalog

## Context and scope

**Implement Stage 06 in `E:\Projects\rust\RustServer.Web`**. The backend is a **separate service/repository** `E:\Projects\rust\RustServer.PluginCatalog` on commit `06d1619f3a1f2105e5de3acc620ff203eafec2b1` (Stage 05, media storage). Do not confuse it with `RustServer.Plugins`, which handles Git, releases, deployment and runtime files.

This stage adds the **catalog list/operator page** in the existing React dashboard: display plugin entries, icons, search, filter by source kind, pagination, create/edit catalog metadata and (if straightforward) delete a plugin with confirmation. **Do not build the full plugin details page, source-editing screen or media gallery/upload UI** — those are Stage 07. The stage is frontend implementation, not a new microservice, new standalone React app or backend change.

The owner-approved roadmap lives in `RustServer.PluginCatalog/owner-view/PROJECT_PLAN.md`: Stage 01–05 completed; Stage 06 = Web catalog; Stage 07 = Web plugin details/source/media; Stage 08 = Platform + Bootstrap; Stage 09 = integration. Preserve this division.

### Ground rules: read before edits

1. Read **all of** `RustServer.Web/AGENTS.md` and current Web routing, navigation, styling, API client/configuration, testing patterns, and README. Treat existing components and operators' workflows as authoritative for presentation consistency.
2. Read `RustServer.PluginCatalog/AGENTS.md`, `owner-view/PROJECT_PLAN.md`, `docs/api-contract.md`, `docs/data-model.md` and the Stage 05 README; this service **owns** the catalog API. When available, start the **Development** Catalog API and inspect `GET /openapi/v1.json` to cross-check methods, payloads, exact enums and nullability. The published API descriptions, not backend internal methods, are the frontend contract. If Development OpenAPI cannot be fetched, document that limitation; do not invent missing fields.
3. Review the relevant `rust-server-platform/docs/ui/` frontend UI contracts and shared changelog conventions, per `RustServer.Web/AGENTS.md`, including the newest `docs/changelog/iteration-NNN.md` and template. If UI docs have old assumptions about Plugins/catalog, report discrepancies; do not silently rewrite platform UI contracts.
4. If you touch the existing Plugins management UI, read the current `RustServer.Plugins` use-case/architecture/API material required by Web `AGENTS.md`. Catalog never gains Plugins deployment privileges by implication.
5. Inspect `git status` in **each** repo you plan to touch. Do not remove, stage or commit the owner's existing untracked files, especially `RustServer.PluginCatalog/owner-view/prompts/`. Leave unrelated changes intact.

## Target navigation and UX

- Keep one `RustServer.Web` React application and the existing `/dashboard` basename. Put the catalog under the **Plugins** UI surface, e.g. route `/dashboard/plugins/catalog`, next to the existing management screen `/dashboard/plugins`. Provide a discoverable **Catalog** link/subnavigation from the Plugins surface (and convenient return to Management). Do **not** create a new top-level dashboard section or replace Plugins deployment workflows.
- Use the current design system, responsive layout, keyboard/accessible labels. The catalog is a **library of known plugins**, not a list of installed plugins. Never show Installed / Update available / Running / Staging / Production indicators unless the appropriate published cross-service contract exists (it does not here).
- Page content: title and short explanatory description; search field; `sourceKind` filter using published enum values; page navigation with backend totals; rows/cards showing icon thumbnail (or accessible placeholder), plugin name, short description, `currentKnownVersion` (label clearly **catalog-known version**), `lastPublishedAt` (if known), and appropriate actions. Preserve backend order; don't perform client-side full-list filtering pretending to search globally.
- Loading, empty (including no matches), error and retry states. Debounce search sensibly; reset page on filter/search change; prevent stale network responses from replacing newer results (AbortController or equivalent). Pagination uses published `page`/`pageSize`, `totalItems`/`totalPages` and limits; don't load the entire catalog by looping pages.
- Add a minimal **Create Plugin** and **Edit Plugin** form/dialog on this page, using the **published** CreatePluginRequest / UpdatePluginRequest (`name`, `slug`, `description`, `currentKnownVersion`, `lastPublishedAt`). Be explicit about required fields and nullable fields; let the backend remain authoritative for normalization/uniqueness. Do not create a separate global forms framework. Show save progress, close on success and refresh the proper page/list; surface `409 slug_conflict` and validation errors readably.
- If implementing Delete Plugin now, require confirmation and honor `409 plugin_has_media` by explaining that media must first be removed (Stage 07 UI). Never assume DELETE cascades files. Otherwise explicitly record Delete UI as deferred without claiming it exists.
- From the list, a link to a future detail view may be provided only if its route is actually handled; do not create dead links or fake detail data. Complete detailed description/sources/gallery/editable media is **Stage 07**.
- Initial catalog with no uploaded icon must render cleanly. **Selected icon comes only from `PluginSummaryDto.icon`**, not the gallery and not external web image scraping.

## API integration: exact boundary

Create an explicit, independent client boundary under `src/shared/api/`, e.g. `plugin-catalog-client.ts` and `plugin-catalog-types.ts`, consistent with repository conventions. Do not mix with `plugins-client.ts`, Control or Versions. Reuse existing HTTP/error utilities and feature-level API adapter injection for tests where suitable.

The current **Stage 05 `RustServer.PluginCatalog/docs/api-contract.md`** defines these relevant operations:

- `GET /api/plugins?page=...&pageSize=...&search=...&sourceKind=...` → `{ items, page, pageSize, totalItems, totalPages }`;
- `POST /api/plugins` → `201 PluginDetailsDto`;
- `GET /api/plugins/{pluginId}` → `PluginDetailsDto` (if needed for an edit form);
- `PUT /api/plugins/{pluginId}` → `PluginDetailsDto`;
- `DELETE /api/plugins/{pluginId}` → `204`, `409 plugin_has_media` possible (optional Stage 06 UI);
- `PluginSummaryDto`: `id`, `name`, `slug`, `shortDescription`, `currentKnownVersion`, `lastPublishedAt`, `updatedAt`, `icon` nullable;
- `PluginIconDto`: `id`, `accessUrl`, `thumbnailAccessUrl`, `contentType`;
- `sourceKind`: exactly `Website`, `GitHub`, `Codefling`, `UMod`, `Documentation`, `VersionLookup`;
- errors: `application/problem+json` with machine-readable `code`, including `validation_failed` and `slug_conflict`.

Mirror exact API naming, types, nullability, capitalization, and actual OpenAPI. Do not expose/store `StorageKey` or physical filesystem paths. A local icon `thumbnailAccessUrl` is **API-root-relative**, such as `/api/plugins/{id}/media/{mediaId}/content?variant=thumbnail`. The browser must request it via the **Catalog's API prefix** (see below), NOT against the dashboard origin root `/api/...` and NOT against `RustServer.Plugins`. Resolve the URL robustly; do not double-prefix or accidentally treat a backend path as a browser navigation link. External URLs are not needed for a selected icon (it must be local Icon media).

Do not implement upload/multipart, gallery endpoints, `PluginSource` CRUD, SetIcon, or media deletions in Stage 06. No Rust server file scanning, automatic version checks, downloading plugins, install/deploy, Git or release actions.

### Frontend API address: prepare for Platform Stage 08

`RustServer.Web` currently uses **fixed same-origin production prefixes** (`/control-api`, `/plugins-api`, etc.), and Vite development proxy addresses come from platform-owned topology. Use an analogous **distinct prefix**, e.g. `/plugin-catalog-api`:

- Add `pluginCatalogApiBaseUrl` to `src/shared/config/environment.ts`, typed consistently with other services. Keep production **pinned to `/plugin-catalog-api`**, regardless of inherited `VITE_*` shell values; do not embed a host/IP/port or credentials in client assets.
- Update `vite.config.ts` build-time `define` and `.env.example` only as needed for that **stable prefix**. Preserve existing four services and config behavior.
- Do **not** hardcode an invented `localhost` / VM port or introduce a mandatory `RUSTSERVER_PLUGIN_CATALOG_URL` that breaks existing Development startup. Actual dev topology, Vite reverse proxy wiring, Nginx route, systemd, service port and Bootstrap ownership belong to `RustServer.Platform` Stage 08. If platform already exposes a *verified* Catalog topology variable and can be read, you may follow it; otherwise leave deployment wiring explicitly deferred. A page showing a clear unavailable state before Stage 08 is acceptable; deterministic UI tests must not depend on the real service.
- Do not add a frontend localhost fallback or a second deployment path. Preserve `/dashboard` routing and base prefix, including behavior on direct navigation or refresh.

### AGENTS and shared changelog

- Update **`RustServer.Web/AGENTS.md`** narrowly to document the new, independent `RustServer.PluginCatalog` published backend contract as the authority for Catalog API, and its separation from `RustServer.Plugins`. Keep all existing plugin management safety rules intact.
- A new operator-visible Catalog page is changelog-worthy. Per `RustServer.Web/AGENTS.md`, add **one** shared entry in `rust-server-platform/docs/changelog/iteration-NNN.md` (next numeric number, following `template.md`) describing Web-only capabilities, deferred Stage 07/08, validation and constraints. Do **not** modify Platform Bootstrap, code, topology, Nginx or UI contract documents in this stage; only the required shared changelog file. If platform files are unavailable, report exactly which convention/entry could not be updated rather than inventing it.
- Update `RustServer.Web/README.md` and any directly relevant local documentation with new UI route and interim connectivity limitation. Do not modify `RustServer.PluginCatalog` API or docs just for a frontend task; the owner's `owner-view/PROJECT_PLAN.md` in Catalog can be updated later at its own milestone, or only if permitted explicitly without unrelated changes.

## Tests and checks: deliberately minimal

Honor the owner's guideline: tests are a **small supporting part** of development. Add **one focused happy-path UI test** (or two very small ones if essential) with a mocked Catalog API, verifying list rendering, icon placeholder/icon URL handling, filtering or creation and list refresh. No large negative/boundary combinatorial suite, no snapshot tests, no new testing framework. Follow current Vitest + React Testing Library patterns.

Run, where possible:

```bash
npm run lint
npm run typecheck
npm run test
npm run build
```

Also manually review route integration, styles, and build-time API base pinning. If a backend/VM or topology is absent, **do not** claim live integration passed; report precisely what was tested and what remains for Stage 08/09.

## Acceptance criteria

1. Navigable Catalog page inside Plugins with list/search/source filter/backend paging, compact icons and clear states; existing `/dashboard/plugins` management remains functional.
2. Create and edit plugin metadata through the published PluginCatalog API; any implemented delete honors the published conflict contract. Stage 07 details/media UI is **not** introduced early.
3. Catalog-specific TypeScript client/types isolated and match current published backend contract; relative icon URL resolves through the correct Catalog prefix.
4. Production prefix is same-origin, pinned and distinct from Plugins. Missing Stage 08 proxy/topology does not crash existing Web startup or falsely show catalog entries.
5. Build/typecheck/lint/tests as available, small happy-path test; one shared changelog entry, Web README/AGENTS updated.
6. Changes limited to `RustServer.Web` plus allowed *shared changelog file* in `rust-server-platform`, nothing in `RustServer.PluginCatalog`, `RustServer.Plugins`, `RustServer.Versions`, or Platform infrastructure.
7. Final report includes: changed file list, exact backend contract files inspected, route and API prefix, testing performed, explicit live-connectivity limitations, known deferred tasks for Stage 07/08, working-tree status **per touched repo**, and commit SHA(s). Do not include pre-existing owner prompt files in commits.

Work in independently reviewable changes. Do not launch Stage 07, Stage 08 or Stage 09.
