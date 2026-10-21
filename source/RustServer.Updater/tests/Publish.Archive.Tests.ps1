$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\scripts\publish.ps1')

function Assert-ArchiveCollisionIsRejected {
    param(
        [Parameter(Mandatory)]
        [string]$PublishDirectory,

        [Parameter(Mandatory)]
        [string]$ArchiveDirectory,

        [Parameter(Mandatory)]
        [string]$Version,

        [Parameter(Mandatory)]
        [string]$Scenario
    )

    try {
        New-PublishArchive `
            -PublishDirectory $PublishDirectory `
            -Version $Version `
            -ArchiveDirectory $ArchiveDirectory | Out-Null
        throw "$Scenario failed. New-PublishArchive did not reject an existing artifact."
    }
    catch {
        if ($_.Exception.Message -notmatch 'Refusing to overwrite existing release artifact') {
            throw "$Scenario failed. Unexpected error: $($_.Exception.Message)"
        }
    }
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("RustServer.Updater.archive-test-" + [guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $testRoot 'publish'
$executorPublishDirectory = Join-Path $testRoot 'executor-publish'
$archiveDirectory = Join-Path $testRoot 'archives'
$expandedDirectory = Join-Path $testRoot 'expanded'
$version = 'v9.9.9-dev.1'
$archiveName = "RustServer.Updater-$version-linux-x64.tar.gz"
$archivePath = Join-Path $archiveDirectory $archiveName
$checksumPath = "$archivePath.sha256"

try {
    New-Item -ItemType Directory -Path $publishDirectory, $executorPublishDirectory, $archiveDirectory | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $publishDirectory 'RustServer.Updater.Api.dll'), 'test payload')

    [System.IO.File]::WriteAllText($archivePath, 'existing archive')
    Assert-ArchiveCollisionIsRejected `
        -PublishDirectory $publishDirectory `
        -ArchiveDirectory $archiveDirectory `
        -Version $version `
        -Scenario 'existing archive'

    [System.IO.File]::Delete($archivePath)
    [System.IO.File]::WriteAllText($checksumPath, 'existing checksum')
    Assert-ArchiveCollisionIsRejected `
        -PublishDirectory $publishDirectory `
        -ArchiveDirectory $archiveDirectory `
        -Version $version `
        -Scenario 'existing checksum'

    [System.IO.File]::Delete($checksumPath)
    [System.IO.File]::WriteAllText((Join-Path $executorPublishDirectory 'RustServer.Updater.Executor.dll'), 'executor payload')
    $systemdDirectory = Join-Path $executorPublishDirectory 'systemd'
    New-Item -ItemType Directory -Path $systemdDirectory | Out-Null
    foreach ($unit in @(
        'rustserver-updater.service',
        'rustserver-updater-executor.socket',
        'rustserver-updater-executor@.service',
        'rustserver-updater-recovery.service'
    )) {
        [System.IO.File]::WriteAllText((Join-Path $systemdDirectory $unit), 'unit payload')
    }

    $artifact = New-PublishArchive `
        -PublishDirectory $publishDirectory `
        -ExecutorPublishDirectory $executorPublishDirectory `
        -Version $version `
        -ArchiveDirectory $archiveDirectory
    New-Item -ItemType Directory -Path $expandedDirectory | Out-Null
    & tar.exe -xzf $artifact.ArchivePath -C $expandedDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Bundled archive could not be expanded.' }

    $bundleManifest = Get-Content -LiteralPath (Join-Path $expandedDirectory 'rustserver-updater.bundle.json') -Raw | ConvertFrom-Json
    if ($bundleManifest.releaseVersion -ne $version -or
        $bundleManifest.updaterEntrypoint -ne 'api/RustServer.Updater.Api.dll' -or
        $bundleManifest.executorEntrypoint -ne 'executor/RustServer.Updater.Executor.dll') {
        throw 'Bundled archive manifest did not bind the API and Executor to one release version.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $expandedDirectory 'api\RustServer.Updater.Api.dll') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $expandedDirectory 'executor\RustServer.Updater.Executor.dll') -PathType Leaf)) {
        throw 'Bundled archive did not contain both required release components.'
    }

    Write-Host 'Passed archive collision and paired-bundle test cases.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        [System.IO.Directory]::Delete($testRoot, $true)
    }
}
