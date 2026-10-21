# Iteration 005 — Installed Rust BuildID reporting

## Goal

Introduce the first real version-state capability in `RustServer.Versions`: report the BuildID of the locally installed Rust Dedicated Server without executing update or SteamCMD operations.

## Changed components

- `RustServer.Versions` Rust version endpoint, provider, and configuration
- `RustServer.Versions` focused Steam app-manifest reader and validation
- `RustServer.Versions` API and manifest-reader test coverage
- `docs/changelog/iteration-005.md`

## Decisions

- `GET /api/versions/rust` reports only `branch` and `installedBuildId`.
- The installed version identity is the Steam BuildID from `AppState.buildid` in the configured `appmanifest_258550.acf` file.
- The manifest reader verifies `AppState.appid == 258550`, requires a numeric non-zero BuildID, and fails clearly for unreadable, malformed, incomplete, or wrong-app manifests.
- `RustVersions:AppManifestPath` and `RustVersions:Branch` are normal ASP.NET Core configuration keys; `Branch` defaults to `public`.
- `RustServer.Versions` does not start SteamCMD, invoke external processes, query remote Steam metadata, or call `RustServer.Control`.
- The previous generic VDF tree parser was replaced with a focused reader that handles the `AppState` object, normal whitespace, comments, quoted values, and nested manifest objects.

## Current capability

With `RustVersions:AppManifestPath` configured for a local Rust Dedicated Server manifest, `RustServer.Versions` returns the installed BuildID through `GET /api/versions/rust`. The endpoint remains available through Development Swagger on port `5113`.

The implementation was verified with `dotnet build` (zero warnings and errors) and `dotnet test` (5 passed tests).

## Out of scope

- Available/latest Rust BuildID discovery
- SteamCMD, Steam Web APIs, external commands, or remote metadata queries
- Update execution or communication with `RustServer.Control`
- `availableBuildId` and `updateAvailable` response fields
- Oxide/uMod and plugin version discovery
- Background polling, caching, persistence, or version history
