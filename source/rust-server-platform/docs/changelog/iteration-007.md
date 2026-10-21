# Iteration 007 — Rust WebRCON package extraction

## Goal

Extract the verified, generic Rust WebRCON transport from `RustServer.Control` into the separate `RustServer.Rcon` package without changing observable Control behavior.

## Changed components

- New `RustServer.Rcon` repository with a packable `net10.0` library and protocol-focused test project
- `RustServer.Rcon` public command-client contract, command response, RCON options, exceptions, and WebRCON client
- `RustServer.Control` API, Infrastructure, and test-project package references to `RustServer.Rcon` version `0.1.0`
- `RustServer.Control` removal of its duplicate generic RCON contracts, WebSocket transport, client implementation, and protocol tests
- `docs/changelog/iteration-007.md`

## Decisions

- `RustServer.Rcon` owns only Rust WebRCON connection and command execution; it has no reference to `RustServer.Control` and does not contain lifecycle semantics.
- The package preserves the existing WebRCON protocol behavior: password escaping in the WebSocket URI, JSON serialization, `Identifier` response correlation, unrelated-response handling, cancellation, connection and command timeouts, response-size limits, response parsing, and sanitized transport failures.
- The configuration contract is unchanged. The section remains `RustServerRcon` with the existing `Host`, `Port`, `Password`, `ConnectTimeout`, `CommandTimeout`, and `MaxResponseBytes` properties and defaults.
- Existing deployment configuration remains compatible with `RustServerRcon__Host`, `RustServerRcon__Port`, `RustServerRcon__Password`, and the corresponding timeout and response-size environment variables.
- `RustServer.Control` uses the package through NuGet `PackageReference`, not a permanent cross-repository `ProjectReference`.
- Package version `0.1.0` was produced only to a local NuGet folder feed. It was not committed or published to a remote registry.
- Docker Compose lifecycle control, readiness orchestration, `status` and `sv quit` semantics, operation tracking, mutation locking, HTTP error mapping, the development command endpoint, and its 16 KiB response cap remain in `RustServer.Control`.

## Current capability

`RustServer.Rcon` provides the same generic Rust WebRCON command execution previously embedded in Control. Its extracted `WebRconServerCommandClientTests` suite contains 11 passing transport tests.

`RustServer.Control` restores `RustServer.Rcon` version `0.1.0` from the local feed and retains its API and lifecycle behavior. Validation completed with zero-warning, zero-error Release builds; `RustServer.Rcon` passed 11 tests, and `RustServer.Control` passed 54 tests. A real Ubuntu VM smoke test was not run for this extraction.

## Out of scope

- Rust running-version discovery
- Oxide/uMod and plugin version discovery
- Changes to `RustServer.Versions`
- Changes to `rust-server-platform`, including `run-control.sh`, `.env` loading, RCON secrets, or shared environment helpers
- New WebRCON features, persistent connections, event streaming, or lifecycle semantics in `RustServer.Rcon`
- Remote NuGet registries, GitHub Packages, package publishing, tags, or commits
