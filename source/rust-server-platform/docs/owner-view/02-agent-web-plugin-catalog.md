# Agent task 2 — RustServer.Web frontend

**Repository:** `sysdba925/RustServer.Web`.  
**Required UX spec:** `rust-server-platform/docs/ui/plugin-catalog-card.md`.  
**Do not begin API-consuming edits until backend task 1 is implemented and its public contract updated.** Read `RustServer.Web/AGENTS.md` and `RustServer.PluginCatalog/docs/api-contract.md` (+ Development OpenAPI as instructed).

## Goal

Replace the vertically stretched Create/Edit flow with a compact shared plugin form while preserving existing media, source and Markdown functionality; add Delete with confirmation to Details.

## Implementation

1. One **shared Add/Edit form** (different initial values and submit endpoints), targeting a desktop 16:9 workspace *with sidebar*; compact ~2:1 columns, header-level Cancel / Save. No routine vertical scrolling at this target desktop viewport; responsive smaller displays may scroll.
2. **All fields visible, all optional except Name**:
   - Name, Author;
   - Source URL(s), Site selection from managed Site list, optional source labels;
   - Description, Notes, **Source description with original functional Edit Markdown / Preview component** (preserve formatting and actual renderer; compact with expand-on-demand);
   - optional Icon upload/change/remove;
   - Known version, Price + Currency, publication date, Tags/Folders multiselect.
3. Known version editor: three numeric segments separated by fixed dots. Support arbitrary digit counts; `.`/`,`/ArrowRight = next segment, ArrowLeft and Backspace at segment start = previous; pasted semantic numbers split across segments. Missing entire version = null, no invented zeros. Do not silently rewrite incompatible existing version strings; show them safely and ask for explicit normalization if edited.
4. Publication date: popover widget selecting **year → month → day**, Today / Clear, **no time selection**; send backend date-only string `YYYY-MM-DD`, avoid timezone conversions.
5. URL + Site + Tags/Folders: populated and validated from current backend contract. In Create/Edit they must be editable; in Details only read-only linked data. Do not use invented `Installed` runtime status from manual tags. Only Name required; for URL-less create you may show a nonblocking warning, not a required-field error.
6. Icon: queue selection before saving; save core card then use existing media/selected-icon APIs. Represent partial failure explicitly and allow safe retry, no fake atomicity with file storage.
7. **Details**: preserve icon/name/author, read-only Description, rendered Source description, Notes, Library metadata, and functioning **Media gallery** (Upload image / Add external media). Remove inline Add source from Details; use Edit instead. Header contains **Back to catalog / Edit plugin / Delete**.
8. Delete: click opens accessible confirmation showing exact plugin name and what associated data/media will be removed. Cancel works. Confirm invokes only the **published backend explicit delete-with-media capability**; handle loading, failure, and post-success navigation. Never perform unrelated runtime uninstall. Do not fake success in UI.
9. Existing JS clients must be updated to actual backend DTOs; no handwritten assumption about API. Do not regress Edit, Gallery, Markdown, tags, folders, source links, or existing list rendering.
10. Update Web's owned docs/changelog in line with `AGENTS.md`, run available checks, provide screenshots or precise viewport verification where feasible. Don't publish releases or push without approval.

## Completion report

State which components were reused, which layout constraints were verified, what API contract was consumed, and any deferred limitations. Keep changes focused; do not rebuild the entire Plugins UI or introduce a new design system.
