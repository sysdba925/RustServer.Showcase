# Iteration 010 — Oxide version reporting

## Goal

Expose installed, running, and latest available Oxide.Rust versions through a dedicated RustServer.Versions endpoint without changing update behavior or Rust version reporting.

## Changed components

- `RustServer.Versions` now exposes `GET /api/versions/oxide`.
- Dedicated Oxide installed, running, and available-version readers plus an Oxide-specific provider and REST model.
- `RustServer.Rcon` NuGet package `0.1.0` is consumed by RustServer.Versions for the running-version query.
- `OxideVersions:AssemblyPath` configuration is added alongside the existing Rust-version and RCON settings.
- Versions service bootstrap derives the Oxide DLL path from `RUST_SERVER_PATH`, reuses `.rcon-password`, and supplies the same RCON host/port/password settings used by Control.
- The Versions systemd unit is now installed with mode `0600`, because it contains the RCON password environment setting.
- Unit and API tests cover installed assembly metadata, GitHub release validation, WebRCON parsing/unavailability, provider comparison logic, and both running/stopped endpoint contracts.

## Behavior

The endpoint returns this shape:

```json
{
  "installedVersion": "2.0.7726",
  "runningVersion": "2.0.7726",
  "availableVersion": "2.0.7726",
  "updateAvailable": false
}
```

- `installedVersion` is read directly from the configured `Oxide.Rust.dll` assembly metadata using `AssemblyName.GetAssemblyName`; it is normalized from four components such as `2.0.7726.0` to the Oxide three-component form `2.0.7726`.
- `runningVersion` is read only from the explicit `Oxide.Rust Version:` line returned by the shared WebRCON client's `oxide.version` command.
- `availableVersion` is read from GitHub Releases at `OxideMod/Oxide.Rust/releases/latest`. The release must contain a valid three-component `tag_name` and the `Oxide.Rust-linux.zip` asset.
- `updateAvailable` compares typed installed and available versions. It intentionally does not depend on the currently running version.
- When WebRCON is unavailable because Rust is stopped, `runningVersion` is `null`; installed and available versions are still returned. Invalid RCON configuration and malformed `oxide.version` output remain explicit logged failures.

## Decisions

- No cached version file, assembly loading, file-time check, updater invocation, RCON reimplementation, or project reference to the RustServer.Rcon repository was introduced.
- Installed, running, and available data are deliberately separate to represent an updated-on-disk Oxide installation that has not yet been loaded by a server restart.
- The production assembly path is supplied only by the Versions systemd bootstrap as `$RUST_SERVER_PATH/RustDedicated_Data/Managed/Oxide.Rust.dll`; application code does not derive or hard-code `/srv/rust/server`.
- The existing `.rcon-password` remains the only RCON secret. No password is returned by the API or written to a new secret file.

## Validation

- `dotnet build RustServer.Versions.sln --configuration Release --no-restore -p:TreatWarningsAsErrors=true` completed with zero warnings and zero errors.
- `dotnet test RustServer.Versions.sln --configuration Release --no-build --no-restore` passed 32 of 32 tests.
- `bash -n scripts/bootstrap.d/95-install-rustserver-versions-service.sh` passed.

## Out of scope

- Rust running-version support
- Oxide/plugin update automation, caching, databases, version-marker files, or combined Rust/Oxide workflows
- Changes to RustServer.Control, RustServer.Rcon, XSite, Terraform, commits, pushes, tags, or remote publication
- A live VM/RCON/GitHub validation run from the Windows development workstation
