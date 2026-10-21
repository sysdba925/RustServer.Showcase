# Iteration 011 — Running Rust server version reporting

## Goal

Expose metadata from the currently running Rust Dedicated Server through the existing Rust version endpoint, while preserving the distinct installed and available Steam BuildID reporting.

## Changed components

- `RustServer.Versions` now extends `GET /api/versions/rust` with a nullable `running` object.
- A dedicated `IRustRunningVersionReader` and `RconRustRunningVersionReader` execute the shared WebRCON client's `version` command.
- A typed `RustRunningVersion` model represents the running server's protocol, build date, Unity version, changeset, and branch.
- `RustVersionProvider` reads the running metadata independently of the existing Steam app-manifest and Steam PICS BuildID readers.
- Unit and API tests cover valid parsing, malformed responses, RCON unavailability and configuration errors, provider behavior, and running/stopped endpoint contracts.

## Behavior

The endpoint now returns this shape when Rust is running:

```json
{
  "branch": "public",
  "installedBuildId": 12345678,
  "running": {
    "protocol": "2633.288.1",
    "buildDate": "2026-09-22T07:48:10",
    "unityVersion": "6000.3.15x1-11",
    "changeset": 165217,
    "branch": "release"
  },
  "availableBuildId": 12345678,
  "updateAvailable": false
}
```

- `running` is parsed only from the explicit `Protocol:`, `Build Date:`, `Unity Version:`, `Changeset:`, and `Branch:` lines in the `version` response.
- `Build Date` uses the invariant `MM/dd/yyyy HH:mm:ss` format and is serialized as an ISO-8601 date-time value.
- A missing, duplicate, empty, or malformed required line is logged and fails explicitly.
- If WebRCON is unavailable because Rust is stopped, `running` is `null`; installed and available BuildIDs are still returned.
- `RustServerRconConfigurationException` remains an explicit error and is not treated as a stopped server.
- `updateAvailable` remains `installedBuildId != availableBuildId`; it does not depend on running-process metadata.

## Decisions

- Steam BuildIDs and running metadata are deliberately separate. Protocol, changeset, and build date are not converted or inferred as Steam BuildIDs.
- The existing `RustServerRcon` configuration, `IServerCommandClient`, `WebRconServerCommandClient`, and password handling are reused. No RCON client, configuration section, environment variable, or secret handling was added.
- No changes were required in `rust-server-platform`, `RustServer.Control`, or `RustServer.Rcon` implementation code.

## Validation

- `dotnet build RustServer.Versions.sln --configuration Release --no-restore -p:TreatWarningsAsErrors=true` completed with zero warnings and zero errors.
- `dotnet test RustServer.Versions.sln --configuration Release --no-build --no-restore` passed 41 of 41 tests.
- Manual VM verification: `curl http://127.0.0.1:5113/api/versions/rust`.

## Out of scope

- Mapping running metadata to Steam BuildIDs
- Persistence, caching, version marker files, Rust update behavior, or a generic component-version framework
- Oxide, plugin, Control, RCON, XSite, Terraform, commit, push, tag, or remote-publication changes
