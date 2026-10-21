# Iteration 065 — StaticWeb release publishing

## Goal

Allow `RustServer.Web` to publish a prebuilt React/Vite distribution that the
existing Updater, Deployment Executor, and Stage 04 Nginx boundary can deploy
as one StaticWeb release lifecycle.

## Added and updated

- `RustServer.Web` now has a repository-local publisher which performs a
  locked dependency install, TypeScript verification, existing frontend tests,
  and the Vite production build before packaging only `dist/`.
- The publisher emits the Updater asset convention
  `RustServer.Web-<tag>-linux-x64.tar.gz` and a standard SHA-256 sidecar,
  verifies root `index.html` and archived distribution contents, and uploads
  both assets to the matching GitHub Release.
- Development prereleases use `vX.Y.Z-dev.N`; Production uses `vX.Y.Z` with
  explicit interactive confirmation. The publisher verifies a clean,
  synchronized `develop` branch through authenticated GitHub CLI APIs and
  retains the newest two releases only after a successful creation.
- Production browser builds use only the Stage 04 same-origin API paths
  `/control-api`, `/versions-api`, `/plugins-api`, and `/snapshots-api`.
  Vite's topology-driven development proxy remains separate.

## Current capability

The release archive can be prepared, validated, staged, activated, checked
through Nginx's release-identifying readiness endpoint, and rolled back by the
existing Updater lifecycle. Publishing itself has no SSH or production-server
deployment step; Node and Vite remain build-machine tools only.

## Deliberately deferred

- No production deployment or migration of an already installed root-owned
  `applications.ini` was performed.
- TLS termination, firewall policy, and isolated Ubuntu end-to-end activation
  remain platform/environment validation work.

## Validation

- RustServer.Web: `npm ci`, `npm run typecheck`, `npm run test` (197 passed),
  and `npm run build`.
- Verified generated assets contain no development backend topology or backend
  port values, and verified the archive root and SHA-256 sidecar with the Web
  publisher's archive checks.
- RustServer.Updater:
  `dotnet test RustServer.Updater.slnx --no-restore --filter
  "FullyQualifiedName~PackagePreparationServiceTests.PrepareAsync_StaticWebPreservesManifestAndRejectsTampering"`
  (1 passed).

## Decisions

- StaticWeb continues to use the common GitHub Release asset contract instead
  of a Web-specific deployment channel or server-side build.
- Release readiness remains owned by Stage 04 Nginx and its dynamic
  `X-RustServer-Release` marker; the Web package does not add a static header
  or alternate health mechanism.
