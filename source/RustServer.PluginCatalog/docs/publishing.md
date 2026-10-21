# Publishing GitHub Releases

The Visual Studio Development and Production profiles publish a framework-dependent
`net10.0` `linux-x64` archive for the RustServer Updater. The publish output includes
the Catalog-owned `provisioning/` scripts, configuration, host templates and
PostgreSQL SQL through the API project. The publisher checks the required assets
and rejects shell scripts with CRLF before archiving; Git attributes also require LF.

Publishing requires a clean `develop` branch tracking `origin/develop` at its current
GitHub commit, GitHub CLI authenticated as `sysdba925`, Git, `tar.exe` and .NET 10.
The publisher selects the next version from GitHub tags: Development uses
`vX.Y.Z-dev.N`, and Production uses `vX.Y.Z`. Production requires confirmation.
Existing GitHub releases are not deleted automatically.

For a dry run, use the desired Visual Studio profile with `GitHubReleaseDryRun=true`,
or run:

```powershell
dotnet publish src/RustServer.PluginCatalog.Api -c Release -r linux-x64 `
  -p:PublishProfile=Development -p:GitHubReleaseDryRun=true
```

A dry run still checks GitHub and creates an archive and SHA-256 sidecar in
`artifacts/releases/`, but does not create a GitHub Release. Existing local
artifacts are not overwritten. The profiles pass MSBuild's intermediate publish
output to the publisher, with its trailing directory separator removed.

Updater prepares the verified release and on first Install invokes the
Catalog-owned `provisioning/provision.sh install` for host, database and media.
Update and Rollback do not repeat provisioning. The publisher does not connect
to a deployment host.
