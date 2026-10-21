# Publishing GitHub Releases

The existing Visual Studio publish profiles are the release entry points:

- `Development.pubxml` creates a GitHub **pre-release**.
- `Production.pubxml` creates a regular GitHub **release** and displays a Windows confirmation dialog before any GitHub write.

Both profiles build the API and Deployment Executor as `Release`, `linux-x64`, framework-dependent binaries. The profile removes its API publish directory before `Publish`, publishes the paired executor, then invokes [`scripts/publish.ps1`](../scripts/publish.ps1). The script packages both already-published components and the required systemd resources as one archive; it never assigns an independent release version to the executor.

## Preconditions

Before creating an archive, the script requires all of the following:

- the current branch is `develop`;
- the working tree is clean;
- `origin` is `sysdba925/RustServer.Updater`;
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

## Runtime-configuration compatibility

Initial provisioning or migration may install the first complete archive and
migrate the application registry to `/etc/rustserver/updater/applications.ini`.
The archive's `rustserver-updater.bundle.json` binds the API and executor to the
same release tag. After that first activation, only the bundled Updater executor
may update or recover the installation; Bootstrap is not a runtime dependency.

## Artifacts and retention

The script creates these ignored local files in `artifacts/releases/`:

- `RustServer.Updater-vX.Y.Z[-dev.N]-linux-x64.tar.gz`
- a sibling `.sha256` file in standard `SHA256  filename` format

The archive has `api/`, `executor/`, and `systemd/` directories plus the bundle
manifest. Publishing fails if either component or any required systemd resource
is absent.

It uploads both through `gh release create`, targeting the verified commit. Only after that command succeeds, it deletes older GitHub Releases in the same channel so that two remain. Release deletion deliberately leaves Git tags intact, which prevents any historical version from being reused. A failed create never triggers retention cleanup.

## Safe local verification

Run the PowerShell version tests:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Publish.Versioning.Tests.ps1
```

To exercise an actual profile without creating or deleting GitHub Releases, add `GitHubReleaseDryRun=true`. It still publishes, validates all Git preconditions, fetches tags through `gh`, creates the archive and checksum, and prints the proposed version.

```powershell
dotnet publish src/RustServer.Updater.Api -p:PublishProfile=Development -p:GitHubReleaseDryRun=true
```

The dry run needs a clean, synchronized `develop` checkout, just like a real release.
