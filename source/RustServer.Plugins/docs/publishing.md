# Publishing GitHub Releases

Visual Studio publish profiles are the release entry points:

- `Development.pubxml` creates a GitHub prerelease.
- `Production.pubxml` creates a GitHub release after a Windows confirmation dialog.

Both profiles publish the application as a framework-dependent `Release` build
for `net10.0` and `linux-x64`. After MSBuild finishes `dotnet publish`, the
profile runs the repository-local [`scripts/publish.ps1`](../scripts/publish.ps1).
The script packages those already-published Linux files; it does not invoke
`dotnet publish` itself.

## Preconditions

Before creating an archive, the script requires all of the following:

- the current branch is `develop`;
- the working tree is clean;
- `origin` is `sysdba925/RustServer.Plugins`;
- the branch tracks exactly `origin/<current branch>`;
- local `HEAD` equals the current remote branch tip; and
- `gh` is installed and already authenticated for `github.com` as `sysdba925`.

Authentication remains in the existing GitHub CLI credential store. The script
never starts an interactive login and never writes a token or credential to the
repository.

Remote branch state is resolved through the authenticated GitHub CLI API:

~~~text
gh api repos/{owner}/{repo}/git/ref/heads/{branch} --jq .object.sha
~~~

No release path uses a network Git command. In particular, it does not use
`git ls-remote`, preventing the Git Credential Manager sign-in dialog that can
otherwise appear during Visual Studio publication.

## Version policy

Only tags matching these formats participate in version selection:

| Channel | Tag format | Selection |
| --- | --- | --- |
| Development | `vMAJOR.MINOR.PATCH-dev.N` | Uses the next production patch and increments `N`. With no release tags this starts at `v0.1.0-dev.1`. |
| Production | `vMAJOR.MINOR.PATCH` | Starts at `v0.1.0` and increments the highest stable patch. |

After `v0.1.0` is released, Development starts the next line at
`v0.1.1-dev.1`. Tags are read through GitHub CLI before selecting a version and
an existing tag is never reused.

## Artifacts and retention

The script creates these ignored local files in `artifacts/releases/`:

- `RustServer.Plugins-vX.Y.Z[-dev.N]-linux-x64.tar.gz`
- a sibling `.sha256` file in standard `SHA256  filename` format

The archive root is the ready-to-deploy Linux x64 publish output. It must
contain `RustServer.Plugins.dll`, its `.deps.json`, and its
`.runtimeconfig.json`; every published file is checked for inclusion. Local
environment files, user publish settings, conventional secret files, and JSON
configuration containing configured secrets are rejected before packaging.

Both artifacts are uploaded by `gh release create` and target the verified
commit. Only after that succeeds, retention deletes older releases in the same
channel so that the two newest remain. Git tags are deliberately retained, so
a historical version cannot be reused. A failed release creation never starts
retention cleanup.

## Dry run

Set `GitHubReleaseDryRun=true` in the Visual Studio/MSBuild publish invocation
to check authentication, Git state, publish output, archive contents, and the
checksum without creating or deleting GitHub Releases:

~~~powershell
dotnet publish src/RustServer.Plugins -p:PublishProfile=Development -p:GitHubReleaseDryRun=true
~~~

The dry run still needs a clean, synchronized `develop` checkout.

## Local verification

The PowerShell tests mock GitHub calls and never create a release:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Publish.Versioning.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Publish.Archive.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Publish.Safety.Tests.ps1
~~~
