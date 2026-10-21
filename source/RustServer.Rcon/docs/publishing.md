# Publishing GitHub Releases

The Visual Studio publish profiles are the GitHub Release entry points:

- `Development.pubxml` creates a GitHub **pre-release**.
- `Production.pubxml` creates a regular GitHub **release** and displays a Windows confirmation dialog before any GitHub write.

Both profiles publish the executable `RustServer.Rcon.Grpc` service as `Release`, `net10.0`, `linux-x64`, framework-dependent. The profile removes its publish directory before `Publish`, then invokes [`scripts/publish.ps1`](../scripts/publish.ps1) only after the publish target completes. The script packages the already-published service files; it never invokes `dotnet publish` itself. The shared `RustServer.Rcon` library is included in the service deployment as a dependency and is not published as a separate release artifact.

## Preconditions

Before creating an archive, the script requires all of the following:

- the current branch is `develop`;
- the working tree is clean;
- `origin` is `sysdba925/RustServer.Rcon`;
- `HEAD` exactly matches the current tip of its tracked remote branch; and
- `gh` is installed and already authenticated for `github.com`.

Authentication stays in the local GitHub CLI credential store. No token or credential is written to this repository.

## Version policy

Only tags matching these formats participate in selection:

| Channel | Tag format | Selection |
| --- | --- | --- |
| Development | `vMAJOR.MINOR.PATCH-dev.N` | Uses the next production patch and increments `N`. With no release tags this starts at `v0.1.0-dev.1`. |
| Production | `vMAJOR.MINOR.PATCH` | Starts at `v0.1.0` and increments the highest stable patch. |

After `v0.1.0` is released, Development starts the next line at `v0.1.1-dev.1`. Existing GitHub tags are queried before a version is selected, and a matching tag is never reused.

## Artifacts and retention

The script creates these ignored local files in `artifacts/releases/`:

- `RustServer.Rcon-vX.Y.Z[-dev.N]-linux-x64.tar.gz` containing the executable service, runtime configuration, and required dependencies;
- a sibling `.sha256` file in standard `SHA256  filename` format.

It uploads both through `gh release create`, targeting the verified commit. Only after that command succeeds does it delete older GitHub Releases in the same channel so that two remain. Release deletion deliberately leaves Git tags intact, preventing historical version reuse. A failed create never triggers retention cleanup.

## Safe local verification

Run the PowerShell tests:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Publish.Versioning.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Publish.Archive.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Publish.Profiles.Tests.ps1
```

To exercise the Development profile without creating or deleting GitHub Releases, add `GitHubReleaseDryRun=true`. It still publishes, validates Git preconditions, queries tags through `gh`, creates the archive and checksum, and prints the proposed version.

```powershell
dotnet publish src/RustServer.Rcon.Grpc -p:PublishProfile=Development -p:GitHubReleaseDryRun=true
```

The dry run needs a clean, synchronized `develop` checkout, just like a real release.
