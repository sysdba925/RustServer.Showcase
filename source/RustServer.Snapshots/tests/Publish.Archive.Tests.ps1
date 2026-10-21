$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\scripts\publish.ps1')

function Assert-Throws {
    param(
        [Parameter(Mandatory)] [scriptblock]$Action,
        [Parameter(Mandatory)] [string]$Message
    )

    try {
        & $Action
        throw "${Message}: no exception was thrown."
    }
    catch {
        if ($_.Exception.Message -eq "${Message}: no exception was thrown.") { throw }
    }
}

function Add-RequiredPublishFiles {
    param([Parameter(Mandatory)] [string]$Directory)

    [System.IO.File]::WriteAllText((Join-Path $Directory 'RustServer.Snapshots.Api.dll'), 'application')
    [System.IO.File]::WriteAllText((Join-Path $Directory 'RustServer.Snapshots.Api.deps.json'), '{}')
    [System.IO.File]::WriteAllText((Join-Path $Directory 'RustServer.Snapshots.Api.runtimeconfig.json'), '{}')
    [System.IO.File]::WriteAllText((Join-Path $Directory 'Dependency.dll'), 'dependency')
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('RustServer.Snapshots.archive-test-' + [guid]::NewGuid().ToString('N'))
try {
    $publishDirectory = Join-Path $testRoot 'publish'
    $archiveDirectory = Join-Path $testRoot 'archives'
    New-Item -ItemType Directory -Path $publishDirectory, $archiveDirectory | Out-Null
    Add-RequiredPublishFiles -Directory $publishDirectory

    $artifact = New-PublishArchive -PublishDirectory $publishDirectory -Version 'v9.9.9-dev.1' -ArchiveDirectory $archiveDirectory
    if ($artifact.ArchiveName -ne 'RustServer.Snapshots-v9.9.9-dev.1-linux-x64.tar.gz') { throw 'Archive name does not match the Snapshots contract.' }
    if (-not (Test-Path -LiteralPath $artifact.ChecksumPath -PathType Leaf)) { throw 'Checksum was not created.' }
    $entries = @(& tar.exe -tzf $artifact.ArchivePath)
    foreach ($requiredFile in $script:RequiredPublishFiles) {
        if ($entries -notcontains "./$requiredFile") { throw "Archive misses required deployable file '$requiredFile'." }
    }
    if ($entries -notcontains './Dependency.dll') { throw 'Archive misses a published dependency.' }

    Assert-Throws -Action {
        New-PublishArchive -PublishDirectory $publishDirectory -Version 'v9.9.9-dev.1' -ArchiveDirectory $archiveDirectory | Out-Null
    } -Message 'Existing archive was accepted'

    [System.IO.File]::WriteAllText((Join-Path $publishDirectory '.env'), 'TOKEN=secret')
    Assert-Throws -Action { Test-SnapshotsPublishDirectory -PublishDirectory $publishDirectory | Out-Null } -Message 'Secret environment file was accepted'
    Remove-Item -LiteralPath (Join-Path $publishDirectory '.env')

    [System.IO.File]::WriteAllText((Join-Path $publishDirectory 'appsettings.Production.json'), '{"Password":"secret"}')
    Assert-Throws -Action { Test-SnapshotsPublishDirectory -PublishDirectory $publishDirectory | Out-Null } -Message 'Configured JSON secret was accepted'
    Remove-Item -LiteralPath (Join-Path $publishDirectory 'appsettings.Production.json')

    Remove-Item -LiteralPath (Join-Path $publishDirectory 'RustServer.Snapshots.Api.dll')
    Assert-Throws -Action { Test-SnapshotsPublishDirectory -PublishDirectory $publishDirectory | Out-Null } -Message 'Missing API assembly was accepted'

    Write-Host 'Passed Snapshots archive, checksum, required-file, dependency, and secret-exclusion checks.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) { [System.IO.Directory]::Delete($testRoot, $true) }
}
