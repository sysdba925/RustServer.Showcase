# Agent task 1 — RustServer.PluginCatalog backend

**Repository:** `sysdba925/RustServer.PluginCatalog`.  
**Required UX spec:** `rust-server-platform/docs/ui/plugin-catalog-card.md` (first place supplied UX file there).  
**Priority:** respect repository `AGENTS.md`, actual `docs/api-contract.md` and current implementation. Do not infer endpoints from the UX spec.

## Goal

Make Add/Edit Plugin able to persist a complete library card, delete a confirmed plugin with media, and import the same optional metadata from JSON. Preserve the existing domain boundaries: PluginCatalog owns catalog metadata; it does not know live Rust server install/deploy state.

## Scope

1. Inspect current `CreatePluginRequest`, `UpdatePluginRequest`, `CreatePluginHandler`, `Source`/`Site`, Tags/Folders, media/selected icon handlers, JSON Import Preview/Commit, Postgres model and export/restore. Briefly report any constraints before implementation; choose the smallest compatible design.
2. Extend **Create/Update** requests to accept optional `sources[]` (`url`, optional `siteId`, optional `label`), `tagIds[]`, `folderIds[]`, along with existing Author, Description, Notes, SourceDescription, Price/Currency, KnownVersion, Date. **Only `name` is required**. Validate refs and persist Plugin + Sources + associations atomically as far as PostgreSQL changes permit; no partially created plugin from invalid source/tag/folder data. Preserve existing endpoints for individual Source/Tag/Folder operations.
3. Date: support publication **calendar date only**, preferably `DateOnly?`/`lastPublishedDate`, serialized `YYYY-MM-DD`. Review and explicitly design the safe migration of existing `LastPublishedAt` (`DateTimeOffset?`) to date-only semantics, along with export/restore compatibility. Preserve existing values and avoid timezone-induced date shifts. This is not a request to make `CreatedAt` or `UpdatedAt` date-only.
4. Known version: preserve existing persisted `CurrentKnownVersion` as optional string; frontend will provide segmented numeric UI for newly edited values. Do not harden backend to 3 components if that silently rejects legacy records; keep validation compatible.
5. Delete with media: current `DELETE /api/plugins/{id}` can return `409 plugin_has_media`. Design and implement an **explicit confirmed/opt-in delete-with-media contract**, e.g. `DELETE /api/plugins/{id}?deleteMedia=true`; existing default defensive behavior should remain compatible. Remove only this plugin's linked sources/associations/media; local-media cleanup must preserve DB consistency and existing orphan logging semantics. Never delete Rust runtime/managed plugin files; do not assume filesystem and DB share a transaction.
6. JSON Import: leave v1 accepted unchanged; add an explicitly versioned compatible extension (e.g. v2) for optional `currentKnownVersion` and `lastPublishedDate`. Keep Preview/Commit validation, duplicate behavior and `createMissing` safeguards. Do not include binary media/icon in JSON import; do not claim a manual `Installed` tag is actual server state.
7. Reuse existing binary Media upload/selected-icon APIs. Do not build multipart replacement for all Create unless genuinely necessary; Web can orchestrate optional Icon upload after saving. Do not add provider-specific behavior in Platform/Updater.
8. Update owned contract documentation and Development OpenAPI; existing consumers must not be broken silently. Perform focused validation per `AGENTS.md`, including existing tests, and report what was not run. Do not publish releases or commit to remote without explicit approval.

## Output

- Concise list of changed API requests/responses/endpoints and date migration strategy.
- Any compatibility impacts for old JSON import/export/restore and existing Web clients.
- Results of relevant validation.
- One clear handoff containing the **actual** contract for RustServer.Web; not a speculative frontend implementation.

**Excluded:** implementing the Web UI, adding backend state of Rust installation, modifying unrelated services, changing Rust plugin deployment behavior.
