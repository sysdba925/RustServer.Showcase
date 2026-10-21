# RustServer.Web

`RustServer.Web` is the frontend control plane for Rust Server Platform.

## Related backend components

- `RustServer.Control`
- `RustServer.Versions`
- `RustServer.Plugins`
- `RustServer.PluginCatalog`
- `RustServer.Snapshots`

## Stack

- React
- TypeScript
- Vite

## Development

Node.js 22.12 or newer is required.

```bash
../rust-server-platform/scripts/dev/with-topology.sh npm install
../rust-server-platform/scripts/dev/with-topology.sh npm run dev
../rust-server-platform/scripts/dev/with-topology.sh npm run check
```

`npm run dev` starts Vite on `RUSTSERVER_WEB_BIND_HOST` at the port from
`RUSTSERVER_WEB_URL`. The application is available at
`RUSTSERVER_WEB_URL/dashboard/`; it is not served from the origin root. The
Vite proxy targets the active backend URLs from the same topology file. This is
development-only behavior.

## Configuration

`/etc/rustserver/topology.env` is the source of HTTP service addresses:

- `RUSTSERVER_CONTROL_URL`
- `RUSTSERVER_VERSIONS_URL`
- `RUSTSERVER_PLUGINS_URL`
- `RUSTSERVER_SNAPSHOTS_URL`
- `RUSTSERVER_PLUGIN_CATALOG_URL`
- `RUSTSERVER_WEB_URL`
- `RUSTSERVER_WEB_BIND_HOST`

`.env.example` contains the stable same-origin API prefixes used by the
browser. They are build-time client values, while the development proxy targets
are Node/Vite startup values. Production fixes all `VITE_*_API_BASE_URL`
values to those same paths and Platform Nginx proxies the existing services
only to loopback services; no topology address, credential, or environment-specific secret is
embedded in browser assets. No runtime frontend configuration service is
present.

The Catalog page uses the independent `/plugin-catalog-api` browser prefix.
It is pinned into production builds; Vite development forwards that prefix to
the required `RUSTSERVER_PLUGIN_CATALOG_URL` topology value. It never uses a
localhost fallback or embeds a topology address in a production asset.

## Plugin catalog

`/dashboard/plugins/catalog` is the Plugins Catalog surface. It lists known
catalog entries with backend search, source-kind filtering, paging, selected
icon thumbnails, and metadata creation/editing. It is deliberately separate
from `/dashboard/plugins` management workflows: Catalog entries do not state
whether a plugin is installed, deployed, or running.

`/dashboard/plugins/catalog/:pluginId` is the Catalog detail surface. It shows
full metadata, Catalog-known version and timestamps, allows manual Source CRUD,
and manages the backend-owned ordered media gallery: local PNG/JPEG/WebP/GIF
uploads, external Video/Preview links, captions, sort order, selected icon, and
deletion. Originals are loaded only when opened; list and gallery thumbnails
resolve local Catalog media through `/plugin-catalog-api`, while external media
links stay absolute and open in a new safe browser context.

Plugin deletion requires an explicit confirmation. It does not cascade media:
the Catalog API returns its published conflict while media remains, and the
operator must remove media first. Catalog version, URLs, and metadata never
imply that a plugin is installed, deployed, or running. The UI does not scrape
sources or automatically check versions.

Platform provisions the Development Catalog topology, persistent PostgreSQL and
media root. Production keeps the backend loopback-only and Nginx protects every
`/plugin-catalog-api/*` request with the existing Admin Auth check. Catalog has
no separate install, deploy, or runtime semantics.

## World snapshots

The World page contains the Snapshots section. It lists newest-first snapshot
summaries and uses the Snapshots API at `GET`/`POST /api/snapshots`,
`GET`/`DELETE /api/snapshots/{id}`, `POST /api/snapshots/{id}/restore`, and
`GET /api/operations/{operationId}`.

Fast snapshots can run while Rust is running and are presented as best-effort.
Consistent creation and restore require Rust to be stopped; the UI never
changes server lifecycle automatically and shows the backend's conflict to the
operator. Create and restore responses are polled locally until they succeed
or fail. Restore and delete both require a confirmation, and successful create,
restore, or delete refreshes the snapshot list.

## Product/UI contract

The initial product and UI design lives in the sibling
`rust-server-platform/docs/ui` documentation. Prototype #1 is the selected
first-screen direction.

## Deployment

Production deployment is handled only by `RustServer.Updater`: Prepare, then
Install or Update, Nginx readiness, and Rollback. The Web release is a prebuilt
static package and never runs Node, Vite, Git, SSH, or a separate deployment
mechanism on the server. See [the publishing guide](docs/publishing.md) for
release creation and artifact details.

### Dashboard path

The application is hosted only below `/dashboard` in Development and
Production. Vite emits its assets below `/dashboard/assets/`, and the browser
router preserves that prefix for the Overview, Server, World, and Plugins URLs.
The production Nginx SPA mapping must serve the application for both
`/dashboard` and `/dashboard/*`.

Backend API prefixes stay at their existing origin-root paths and are not part
of the dashboard base path:

- `/control-api`
- `/versions-api`
- `/plugins-api`
- `/plugin-catalog-api`
- `/snapshots-api`
