# Publishing GitHub Releases

`scripts/publish.ps1` is the release entry point for the prebuilt Web
application. It packages only Vite's `dist/` output; source files, Git data,
development configuration, and `node_modules` are never release inputs.

## Preconditions

The publisher requires a clean `develop` checkout whose `HEAD` equals
`origin/develop`, an `origin` of `sysdba925/RustServer.Web`, Node.js 22.12 or
newer, `npm`, `tar.exe`, and GitHub CLI authenticated as `sysdba925` with
repository access. It queries the remote branch through the authenticated
GitHub API and does not use network Git commands or write credentials.

## Build and package

For either channel the publisher runs, in order:

1. `npm ci`
2. `npm run typecheck`
3. `npm run test`
4. `npm run build`

The production build is hosted below `/dashboard/`; Vite emits static resources
under `/dashboard/assets/`, and client-side navigation remains below
`/dashboard`. The production build has five fixed public, same-origin API bases
outside that SPA path:

- `/control-api`
- `/versions-api`
- `/plugins-api`
- `/plugin-catalog-api`
- `/snapshots-api`

They match the Platform Nginx proxy prefixes. Production does not consume
backend addresses, Vite development topology, credentials, or environment
secrets. The Vite proxy remains development-only and continues to receive its
targets from `/etc/rustserver/topology.env`.

The publisher requires a non-empty `dist/index.html` at the archive root,
rejects reparse points and conventional secret/development paths, archives the
complete distribution tree, verifies every built file is present in the final
archive, and writes a checksum over that final archive. It produces ignored
local artifacts under `artifacts/releases/`:

- `RustServer.Web-vX.Y.Z[-dev.N]-linux-x64.tar.gz`
- `RustServer.Web-vX.Y.Z[-dev.N]-linux-x64.tar.gz.sha256`

The checksum sidecar uses the Updater-compatible `SHA256  filename` format.
The publisher verifies that sidecar against the final archive before DryRun or
publication continues. Each attempt generates files in a temporary directory,
then replaces only the two exact artifact paths calculated for that version.
Consequently, a local artifact from a DryRun or failed publication never blocks
a retry of the same version, while unrelated files are not overwritten.
Because `index.html` is at the archive root, the Stage 02 package preparation
contract selects and stages the complete `dist` tree directly.

## Channels and publishing

Development tags use `vX.Y.Z-dev.N` and publish GitHub prereleases.
Production tags use `vX.Y.Z`; production publication requires an explicit
Windows confirmation dialog. Tags are read from GitHub, existing tags are
never reused, and each successful release targets the verified remote commit.
After a successful creation, the publisher retains the newest two releases in
the selected channel; Git tags remain intact.

Run a dry run to perform all build, package, checksum, GitHub authentication,
and remote-branch checks without creating or deleting a GitHub Release:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Channel Development -DryRun
```

The publisher prints each validation, dependency, typecheck, test, build,
packaging, checksum, and release step. A successful publication ends with its
final version and GitHub Release URL; DryRun reports its calculated version.

Omit `-DryRun` only when the release owner intends to create the release:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Channel Development
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Channel Production
```

The publisher never connects to a production host, invokes SSH, edits
`/opt/rustserver/web/current`, or asks the host to check out Git. Deployment
remains the Updater workflow: Prepare, then Install or Update, readiness
through Nginx, and Rollback when requested or required.
