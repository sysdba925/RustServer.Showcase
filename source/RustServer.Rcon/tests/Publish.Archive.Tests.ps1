$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\scripts\publish.ps1')

function Assert-ArchiveCollisionIsRejected {
    param(
        [Parameter(Mandatory)] [string]$PublishDirectory,
        [Parameter(Mandatory)] [string]$ArchiveDirectory,
        [Parameter(Mandatory)] [string]$Version,
        [Parameter(Mandatory)] [string]$Scenario
    )

    try {
        New-PublishArchive -PublishDirectory $PublishDirectory -Version $Version -ArchiveDirectory $ArchiveDirectory | Out-Null
        throw "$Scenario failed. New-PublishArchive did not reject an existing artifact."
    }
    catch {
        if ($_.Exception.Message -notmatch 'Refusing to overwrite existing release artifact') {
            throw "$Scenario failed. Unexpected error: $($_.Exception.Message)"
        }
    }
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("RustServer.Rcon.archive-test-" + [guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $testRoot 'publish'
$archiveDirectory = Join-Path $testRoot 'archives'
$version = 'v9.9.9-dev.1'
$archiveName = "RustServer.Rcon-$version-linux-x64.tar.gz"
$archivePath = Join-Path $archiveDirectory $archiveName
$checksumPath = "$archivePath.sha256"

try {
    New-Item -ItemType Directory -Path $publishDirectory, $archiveDirectory | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $publishDirectory 'RustServer.Rcon.Grpc'), 'test payload')

    [System.IO.File]::WriteAllText($archivePath, 'existing archive')
    Assert-ArchiveCollisionIsRejected -PublishDirectory $publishDirectory -ArchiveDirectory $archiveDirectory -Version $version -Scenario 'existing archive'

    [System.IO.File]::Delete($archivePath)
    [System.IO.File]::WriteAllText($checksumPath, 'existing checksum')
    Assert-ArchiveCollisionIsRejected -PublishDirectory $publishDirectory -ArchiveDirectory $archiveDirectory -Version $version -Scenario 'existing checksum'

    Write-Host 'Passed 2 archive collision test cases.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) { [System.IO.Directory]::Delete($testRoot, $true) }
}
