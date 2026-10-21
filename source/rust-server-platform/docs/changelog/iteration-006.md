# Iteration 006 — SteamKit2 available Rust BuildID reporting

## Goal

Extend `RustServer.Versions` with read-only discovery of the available Rust Dedicated Server BuildID, replacing the intended SteamCMD-based approach with direct Steam PICS metadata queries through SteamKit2.

## Changed components

- `RustServer.Versions` API contract, asynchronous controller, provider, and dependency registration
- `RustServer.Versions` installed and available BuildID reader abstractions
- `RustServer.Versions` SteamKit2 anonymous PICS reader and structured BuildID extraction
- `RustServer.Versions` manifest-reader simplification and deterministic test coverage
- `docs/changelog/iteration-006.md`

## Decisions

- `GET /api/versions/rust` now returns `branch`, `installedBuildId`, `availableBuildId`, and `updateAvailable`.
- `installedBuildId` continues to come from the configurable local `appmanifest_258550.acf`; `AppState.appid` must be `258550` and `AppState.buildid` must be numeric and non-zero.
- `availableBuildId` comes from SteamKit2 3.4.0 PICS metadata for AppID `258550`, extracted directly from `depots -> branches -> <configured branch> -> buildid`.
- Every lookup has isolated `SteamClient` state: connect, anonymous logon, PICS request, extraction, and disconnect. No shared or persistent Steam connection is retained.
- The asynchronous lookup supports request cancellation and has a 15-second total timeout. Callback processing is cancelled and awaited during cleanup.
- `updateAvailable` is calculated only as `installedBuildId != availableBuildId`.
- The local manifest reader now uses SteamKit2's structured `KeyValue` VDF reader; there is no custom generic VDF parser.
- `RustServer.Versions` does not execute SteamCMD or any external process, download/update Rust, alter the installation, call `RustServer.Control`, or perform lifecycle/RCON actions.

## Current capability

With `RustVersions:AppManifestPath` configured, the API reports the installed local BuildID and the configured branch's currently available BuildID from anonymous Steam PICS metadata. The Development API remains available on port `5113`.

The implementation was verified with `dotnet build` (zero warnings and errors) and `dotnet test` (11 passed tests). A live read-only SteamKit smoke test for AppID `258550`, branch `public`, returned BuildID `25454815` in approximately 2.1 seconds; it matched SteamDB's public BuildID at the time of verification.

## Out of scope

- SteamCMD, Rust downloads, update execution, or installation mutation
- `RustServer.Control` integration, server lifecycle actions, and RCON/running-version discovery
- Steam account credentials, API keys, SteamDB scraping, or alternative metadata sources
- Background refresh, caching, persistent Steam connections, databases, Redis, queues, or version history
- Oxide/uMod and plugin version discovery
