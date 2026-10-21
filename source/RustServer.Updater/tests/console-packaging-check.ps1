# Build the same publish-profile properties, omitting only the release-creation target.
# Archives are produced locally using the production bundle assembler; no GitHub calls.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("updater-console-package-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
. (Join-Path $root 'scripts/publish.ps1')
foreach ($channel in @('Development', 'Production')) {
    $source = Join-Path $root "src/RustServer.Updater.Api/Properties/PublishProfiles/$channel.pubxml"
    [xml]$profile = Get-Content -LiteralPath $source -Raw
    $target = $profile.Project.Target | Where-Object { $_.Name -eq "Publish${channel}GitHubRelease" }
    [void]$target.ParentNode.RemoveChild($target)
    $copy = Join-Path $scratch "$channel.pubxml"
    $profile.Save($copy)
    $api = Join-Path $scratch "$channel/api"
    $executor = Join-Path $scratch "$channel/executor"
    dotnet publish (Join-Path $root 'src/RustServer.Updater.Api/RustServer.Updater.Api.csproj') "-p:PublishProfileFullPath=$copy" --output $api
    if ($LASTEXITCODE -ne 0) { throw "$channel API publish failed" }
    dotnet publish (Join-Path $root 'src/RustServer.Updater.Executor/RustServer.Updater.Executor.csproj') --configuration Release --runtime linux-x64 --self-contained false --output $executor
    if ($LASTEXITCODE -ne 0) { throw "$channel executor publish failed" }
    $artifact = New-PublishArchive -PublishDirectory $api -ExecutorPublishDirectory $executor -Version "v1.0.0-$($channel.ToLowerInvariant())" -ArchiveDirectory (Join-Path $scratch $channel)
    $files = @(tar -tzf $artifact.ArchivePath)
    foreach ($file in @('index.html', 'assets/console.css', 'assets/console.js', 'assets/rust.ico')) {
        if (-not ($files | Where-Object { $_ -match "api/wwwroot/console/$([regex]::Escape($file))$" })) { throw "$channel bundle missing $file" }
    }
    Write-Host "$channel bundle contains all console assets: $($artifact.ArchivePath)"
}
