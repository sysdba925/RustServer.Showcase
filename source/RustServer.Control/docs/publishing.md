# Publishing GitHub Releases

The Visual Studio publish profiles are the release entry points:

- `Development.pubxml` creates a GitHub **pre-release**.
- `Production.pubxml` creates a regular GitHub **release** and displays a Windows confirmation dialog before any GitHub write.

Both profiles build the application as `Release`, `net10.0`, `linux-x64`, framework-dependent. The profile removes its publish directory before `Publish`, then invokes [`scripts/publish.ps1`](../scripts/publish.ps1) only after the publish target completes. The script packages those already-published files; it never invokes `dotnet publish` itself.

## Preconditions

Before creating an archive, the script requires all of the following:

- the current branch is `develop`;
- the working tree is clean;
- `origin` is `sysdba925/RustServer.Control`;
- `HEAD` exactly matches the current tip of its tracked remote branch; and
- `gh` is installed and already authenticated for `github.com` as `sysdba925`.

Authentication is checked non-interactively with the existing GitHub CLI session. The script never invokes `gh auth login` and never opens a browser, login dialog, or interactive GitHub authentication window. If the session is missing or expired, it fails with instructions to authenticate separately:

```powershell
gh auth login --hostname github.com
```

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

- `RustServer.Control-vX.Y.Z[-dev.N]-linux-x64.tar.gz`
- a sibling `.sha256` file in standard `SHA256  filename` format

It uploads both through `gh release create`, targeting the verified commit. Only after that command succeeds, it deletes older GitHub Releases in the same channel so that two remain. Release deletion deliberately leaves Git tags intact, which prevents any historical version from being reused. A failed create never triggers retention cleanup.

## NetworkAccess line-ending safety

The release includes a fixed nftables boot guard and systemd helper unit.
Windows Git checkouts can convert these resources to CRLF even when the
repository files are LF. The Linux `nft -f` parser can reject the resulting
carriage returns as `unexpected junk`, preventing the privileged Helper and
Control API from starting.

The repository uses `.gitattributes` to require LF for `.nft`, `.service`,
and shell scripts. As an additional guarantee, `scripts/publish.ps1` converts
the **published copies** of the guard and service unit to UTF-8 without BOM
and LF-only newlines immediately before producing the release archive.
The script does not modify tracked source files. Missing or empty release
resources reject publication instead of producing a broken artifact.

To verify a dry-run archive, extract the guard from the tarball and inspect
its bytes: there must be no `0D` (carriage return) bytes in the `.nft` file.
On the target Ubuntu machine, check syntax with `sudo nft -c -f <guard-path>`
before starting the Helper.

## Safe local verification

To exercise an actual profile without creating or deleting GitHub Releases, add `GitHubReleaseDryRun=true`. It still publishes, validates all Git preconditions, fetches tags through `gh`, creates the archive and checksum, and prints the proposed version.

```powershell
dotnet publish src/RustServer.Control.Api -p:PublishProfile=Development -p:GitHubReleaseDryRun=true
```

The dry run needs a clean, synchronized `develop` checkout, just like a real release.
