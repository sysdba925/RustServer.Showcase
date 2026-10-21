# RustServer.PluginCatalog — Plugin Card UX Contract

**Status:** Approved UX direction for implementation (2026-10-18).  
**Scope:** Plugin catalog **Create**, **Edit**, **Details**, media gallery, and Delete confirmation; parity with JSON import.  
**Canonical destination:** `rust-server-platform/docs/ui/plugin-catalog-card.md` (add link from the UI index).  
**Source of truth for HTTP:** `RustServer.PluginCatalog/docs/api-contract.md` and the live Development OpenAPI. This document specifies **desired UX**, not currently available API operations.

## 1. Overall layout

- Target: desktop **16:9**, content area **after navigation sidebar**. No routine page scrolling in Create/Edit at this target size. Responsive design may scroll on smaller viewports.
- One compact **two-column** Create/Edit form: left column ≈ 2/3 (name, author, URL/site, descriptions/notes); right ≈ 1/3 (icon, version, price/currency, publication date, tags/folders). Actions in the top row.
- Dense but readable: modest spacing, short textareas by default, expanding/focus/fullscreen Markdown editor for long content. Never truncate or discard long text, and do not remove fields just to avoid scrolling.
- **Create and Edit are the same form component** with different initial state, submit action, and title. Preserve existing data, including unknown/legacy version formats, without silent normalization.
- **Only Name is required** by form/contract. URL, Site, Author, Price/Currency, Version, Publication Date, Markdown content, Tags, Folders, Icon are optional. Price and currency must be provided together if Price is set.
- Non-interactive status facts (installed/deployed/running) must not be invented from manually assigned library tags.

## 2. Create/Edit layout: full set of fields

**Top toolbar**
- Create: `Add plugin` + `Cancel` + `Create plugin`.
- Edit: `Edit plugin` + `Cancel` + `Save changes`.
- Prevent double submit, show validation and operation errors in context.

**Left column**
1. `Name *` — string, only required field.
2. `Author` — optional.
3. `Plugin URL` — optional, prominently placed. Offer to select/add an associated Site; editing supports multiple Sources without navigating to Details.
4. `Site` — **existing managed Sites** (e.g., Codefling, uMod); not a hard-coded enum or category. A Source is a pair/record of URL with optional Site and optional label.
5. `Source label` — optional Source-specific text. Support adding/removing multiple Sources in Edit and (where reasonable) in Create.
6. `Description` — full editable description.
7. `Notes` — personal notes.
8. `Source description` — **existing Markdown editor** with visually grouped `Edit Markdown` / `Preview` toggle; must preserve the actual Markdown renderer, not replace it with a plain-text pseudo-preview. Compact default view; expand on demand.

**Right column**
9. `Icon` — optional local image; show thumbnail, select/change/remove. New Create can queue an icon before the plugin has an ID; upload and selection performed after core creation as part of one **user-visible workflow**.
10. `Known version` — optional segmented `Major . Minor . Patch` UI. Dots are always visible. Each segment accepts arbitrary-length digits; `.` or `,` or `ArrowRight` moves to next segment; `ArrowLeft` moves to previous segment; Backspace at the start of an empty segment navigates back; paste dotted/comma-separated numbers where feasible. Empty **entire** version stays null; never invent `0.0.0`. For a newly entered non-empty value, validate all three segments. Preserve legacy values that don't fit this pattern without silent data loss: inform the user and require explicit changes to normalize.
11. `Price` — optional nonnegative decimal with up to 2 fractional places, paired with 3-letter `Currency` select; do not assume that `Price` means `Purchased`.
12. `Last published date` — optional **date only**, **no hours/minutes/seconds/timezone UX**. Popover calendar supports choosing **year first, month second, day third**, plus Today / Clear. Persist an ISO calendar date `YYYY-MM-DD`; backend date-only contract and existing data migration required; avoid converting a date to a local/UTC instant behind the user's back.
13. `Tags` — multiselect of **library tags** already managed by the catalog, no hardcoded enum; manual `Installed`/`Not installed` tags are labels, not confirmed runtime facts. Do not silently apply `Installed` from import or `Not installed` when Tags were explicitly provided. New item may present a suggested `Not installed` selection, but the user must be able to clear/override it and untagged remains valid.
14. `Folders` — multiselect of existing managed folders.

> Notes: There are 14 **logical groups**, some containing multiple inputs; do not optimize UX by dropping Author, Price, Currency, Source description, Known version, or Icon.

## 3. Details page

**Header**
- `Back to catalog` on left; `Edit plugin` and **`Delete`** on right.
- Plugin icon, Name, Author displayed prominently. No separate inline source-edit button in Details; URL/Site/Tags/Folders edited via the shared Edit form.

**Main column**
- Read-only `Description`, rendered `Source description` (Markdown), `Notes`.
- `Media gallery` stays on **Details**, with **Upload image** and **Add external media** actions and existing gallery functionality (display, icon selection if supported, media management). Additional gallery UI refinements can be deferred.

**Library sidebar**
- Read-only clickable Source URL(s) and Site(s), Price/Currency, Known version, Last published **date only**, Tags, Folders.
- Additional technical metadata: slug, created/updated timestamps (these are actual catalog timestamps and still include time).
- Do not duplicate list of tags/folders unnecessarily; avoid misleading manual tags as server installation state.

## 4. Delete with confirmation

- Explicit `Delete` button in Details header, visually distinct from Edit.
- Press → confirmation dialog with **plugin name**, clear irreversible-warning text, and summary of associated sources/media. `Cancel` returns safely; confirm explicitly triggers deletion; provide progress and failure feedback.
- Current API may reject `DELETE /api/plugins/{id}` when media exists (`409 plugin_has_media`). Implementation must support confirmed deletion of a plugin **including its associated metadata and local/external media** without forcing the user to manually remove all gallery items first.
- Backend must own safe deletion semantics: no silent loss of unrelated files, no stale DB links, no synchronous assumptions of DB/filesystem atomicity. Preserve existing conservative DELETE semantics for old callers when possible; an explicit opt-in option for deleting associated media is acceptable. Follow existing media cleanup/orphan logging rules. Confirm deletion does not remove actual Rust server plugin files or perform an `Oxide` deploy/uninstall.
- On success navigate back to catalog and refresh list. On failure stay on Details and show actionable message. The UI mock must not pretend the backend capability exists until the API contract is updated.

## 5. Save model & API ownership

- **One user-visible Save** for Create/Edit, with all entered library data. Backend should support saving core Plugin + Sources + Tag/Folder links consistently in one request/transaction, rather than a fragile sequence of user-visible partial saves.
- Icon is binary: may require existing media endpoint after plugin creation; orchestrate separately while clearly handling partial-failure/cleanup and reporting. Do not claim SQL + filesystem atomicity.
- Existing Site/Tag/Folder IDs must be validated; missing IDs should produce actionable errors. Avoid inventing new Sites/Tags/Folders without user approval.
- Update/selection of icon in Edit follows existing Media API. Do not replace existing supported media/gallery operations without explicit need.
- API owns DTOs, storage, constraints, date-only model/migration, Delete semantics and contract docs. Web follows the **updated published API**. No frontend mock requests or private DB assumptions.

## 6. JSON Import parity

- Existing JSON Import **v1** remains accepted unchanged. It already handles `schemaVersion`, `plugins[]`, optional Sources (URL/Site/label), Author, Description, Notes, SourceDescription, Price, Tags and Folders; current implementation does **not** import Known version or publication date.
- Extend import format in a backward-compatible way (e.g., explicit **schemaVersion 2**, with appropriate Preview/Commit support) to accept at least `currentKnownVersion` and `lastPublishedDate` as optional fields. Preserve the `YYYY-MM-DD` semantics. Do not require fields absent in Excel; do not fabricate `Installed`, Site, Version or Dates.
- The same domain validation/normalization should apply to manual Create and bulk import where data structures overlap. Existing v1 behavior for duplicates and explicit `createMissing` approval must be preserved.
- Binary Icon/Media file upload is **not** part of simple JSON import. Do not invent local image paths or fetch external media without a separate approved feature.
- Tags imported from Excel remain **user-provided labels**; they are not verified installed/deployed state.

## 7. Acceptance checklist

- At 16:9 with sidebar, Create/Edit displays all key groups without routine vertical scrolling; responsive fallback is allowed.
- Create/Edit show every field named above and preserve the actual Source description Markdown editor / Preview.
- Date widget requires no typing, works year → month → day, stores **date only**.
- Version segmented typing/navigation/paste works, null remains null, malformed/legacy version never silently changed.
- A plugin with URL + Site + Tags/Folders can be created/edited in one user flow; all non-Name fields optional.
- Optional icon can be queued and uploaded; existing gallery stays available in Details.
- Details has Delete with **explicit confirmation**, and full plugin-with-media deletion works under an updated, audited backend contract.
- JSON v1 still works; extended import accepts optional Known version and date, without accidentally marking actual runtime state.

## 8. Delivery sequence

1. Commit and link this UX contract in `rust-server-platform/docs/ui/` as an explicit UI decision, not an API implementation claim.
2. Update **RustServer.PluginCatalog** API/storage/import/delete contracts and release-compatible migration mechanics. Do not change Platform bootstrap or Updater boundaries. Validate API and document precisely.
3. Update **RustServer.Web** once the new backend contract is available. Reuse existing components, preserve Markdown and gallery behavior. Validate desktop compactness and mobile fallback.

**Decision for review before coding:** The requirement “date only” changes an existing `DateTimeOffset` model. Before implementation the backend agent must inspect real PostgreSQL schema and existing export/restore contracts, and document the non-destructive migration/compatibility strategy. No automatic destructive migration.
