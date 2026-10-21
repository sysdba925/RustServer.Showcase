# Publishing GitHub Releases

The existing Visual Studio publish profiles are the release entry points:

- `Development.pubxml` creates a GitHub **pre-release**.
- `Production.pubxml` creates a regular GitHub **release** and displays a Windows confirmation dialog before any GitHub write.

Both profiles build the application as `Release`, `linux-x64`, framework-dependent. The profile removes its publish directory before `Publish`, then invokes [`scripts/publish.ps1`](../scripts/publish.ps1) only after the publish target completes. The script packages those already-published files; it never invokes `dotnet publish` itself.

## Preconditions

Before creating an archive, the script requires all of the following:

- the current branch is `develop`;
- the working tree is clean;
- `origin` is `sysdba925/RustServer.Versions`;
- `HEAD` exactly matches the current tip of its tracked remote branch; and
- `gh` is installed and already authenticated for `github.com`.

Authentication stays in the local GitHub CLI credential store. No token or credential is written to this repository.

## Version policy

Only tags matching these formats participate in version selection:

| Channel | Tag format | Selection |
| --- | --- | --- |
| Development | `vMAJOR.MINOR.PATCH-dev.N` | Uses the next production patch and increments `N`. With no release tags this starts at `v0.1.0-dev.1`. |
| Production | `vMAJOR.MINOR.PATCH` | Starts at `v0.1.0` and increments the highest stable patch. |

After `v0.1.0` is released, Development starts the next line at `v0.1.1-dev.1`. Existing tags are queried from GitHub before a version is selected, and a matching tag is never reused.

## Artifacts and retention

The script creates these ignored local files in `artifacts/releases/`:

- `RustServer.Versions-vX.Y.Z[-dev.N]-linux-x64.tar.gz`
- a sibling `.sha256` file in standard `SHA256  filename` format

It uploads both through `gh release create`, targeting the verified commit. Only after that command succeeds, it deletes older GitHub Releases in the same channel so that two remain. Release deletion deliberately leaves Git tags intact, which prevents any historical version from being reused. A failed create never triggers retention cleanup.

## Safe local verification

Run the PowerShell version tests:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Publish.Versioning.Tests.ps1
```

To exercise an actual profile without creating or deleting GitHub Releases, add `GitHubReleaseDryRun=true`. It still publishes, validates all Git preconditions, fetches tags through `gh`, creates the archive and checksum, and prints the proposed version.

```powershell
dotnet publish src/RustServer.Versions.Api -p:PublishProfile=Development -p:GitHubReleaseDryRun=true
```

The dry run needs a clean, synchronized `develop` checkout, just like a real release.
