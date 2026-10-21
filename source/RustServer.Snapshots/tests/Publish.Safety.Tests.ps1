$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\scripts\publish.ps1')

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('RustServer.Snapshots.publish-safety-test-' + [guid]::NewGuid().ToString('N'))
$script:calls = [System.Collections.Generic.List[string]]::new()
try {
    $publishDirectory = Join-Path $testRoot 'publish'
    $archiveDirectory = Join-Path $testRoot 'archives'
    New-Item -ItemType Directory -Path $publishDirectory, $archiveDirectory | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $publishDirectory 'RustServer.Snapshots.Api.dll'), 'application')
    [System.IO.File]::WriteAllText((Join-Path $publishDirectory 'RustServer.Snapshots.Api.deps.json'), '{}')
    [System.IO.File]::WriteAllText((Join-Path $publishDirectory 'RustServer.Snapshots.Api.runtimeconfig.json'), '{}')

    function Get-GitHubCliPath { 'gh' }
    function Invoke-NativeCommand {
        param([string]$FilePath, [string[]]$Arguments, [string]$FailureHint)

        $call = "$FilePath $($Arguments -join ' ')"
        $script:calls.Add($call)
        if ($FilePath -eq 'tar.exe') {
            $output = @(& tar.exe @Arguments 2>&1)
            if ($LASTEXITCODE -ne 0) {
                throw "tar.exe failed: $(($output | ForEach-Object ToString) -join [Environment]::NewLine)"
            }

            return (($output | ForEach-Object ToString) -join [Environment]::NewLine).Trim()
        }

        switch -Regex ($call) {
            'auth status --hostname github\.com' { return 'Logged in to github.com as sysdba925' }
            'api user --jq \.login' { return 'sysdba925' }
            'api repos/sysdba925/RustServer\.Snapshots --jq \.full_name' { return 'sysdba925/RustServer.Snapshots' }
            'api repos/sysdba925/RustServer\.Snapshots/git/ref/heads/develop --jq \.object\.sha' { return '0123456789abcdef0123456789abcdef01234567' }
            'rev-parse --is-inside-work-tree' { return 'true' }
            'status --porcelain' { return '' }
            'branch --show-current' { return 'develop' }
            'remote get-url origin' { return 'https://github.com/sysdba925/RustServer.Snapshots.git' }
            'config --get branch\.develop\.remote' { return 'origin' }
            'config --get branch\.develop\.merge' { return 'refs/heads/develop' }
            'rev-parse HEAD' { return '0123456789abcdef0123456789abcdef01234567' }
            default { throw "Unexpected native command: $call" }
        }
    }

    $script:releaseCreated = $false
    $script:retentionRan = $false
    function Publish-GitHubRelease { $script:releaseCreated = $true }
    function Remove-ObsoleteGitHubReleases { $script:retentionRan = $true }

    $result = Invoke-Publish -Channel Development -PublishDirectory $publishDirectory -Repository 'sysdba925/RustServer.Snapshots' -RequiredBranch develop -KnownTags @('v0.1.0') -ArchiveDirectory $archiveDirectory -DryRun
    if (-not $result.DryRun -or -not (Test-Path -LiteralPath $result.Artifact.ArchivePath -PathType Leaf)) {
        throw 'Dry Run did not produce the expected local archive result.'
    }
    if ($script:releaseCreated -or $script:retentionRan) {
        throw 'Dry Run attempted a GitHub write or release retention.'
    }
    if (-not ($script:calls | Where-Object { $_ -match 'git/ref/heads/develop' })) {
        throw 'Remote HEAD was not queried with gh api.'
    }
    if ($script:calls | Where-Object { $_ -match 'ls-remote|\bfetch\b|\bpush\b|release create|release delete' }) {
        throw 'Publish safety path used a prohibited network Git command or GitHub write.'
    }

    $script:confirmationRequested = $false
    function Confirm-ProductionPublication {
        $script:confirmationRequested = $true
        throw 'Production confirmation intercepted by test.'
    }

    try {
        Invoke-Publish -Channel Production -PublishDirectory $publishDirectory -Repository 'sysdba925/RustServer.Snapshots' -RequiredBranch develop -KnownTags @('v0.2.0') -ArchiveDirectory (Join-Path $testRoot 'production-archives') | Out-Null
        throw 'Production publishing proceeded without confirmation.'
    }
    catch {
        if ($_.Exception.Message -eq 'Production publishing proceeded without confirmation.') { throw }
        if ($_.Exception.Message -ne 'Production confirmation intercepted by test.') { throw }
    }
    if (-not $script:confirmationRequested -or $script:releaseCreated -or $script:retentionRan) {
        throw 'Production confirmation did not precede release writes.'
    }

    foreach ($profile in @('Development.pubxml', 'Production.pubxml')) {
        $profilePath = Join-Path $PSScriptRoot "..\src\RustServer.Snapshots.Api\Properties\PublishProfiles\$profile"
        $profileText = Get-Content -LiteralPath $profilePath -Raw
        if ($profileText -notmatch 'scripts\\publish\.ps1' -or $profileText -notmatch '<Configuration>Release</Configuration>' -or $profileText -notmatch '<TargetFramework>net10\.0</TargetFramework>' -or $profileText -notmatch '<RuntimeIdentifier>linux-x64</RuntimeIdentifier>') {
            throw "$profile does not invoke the local Linux Release publisher."
        }
    }

    Write-Host 'Passed mocked GitHub authentication, gh-api remote HEAD, Dry Run, production-confirmation, and publish-profile checks.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) { [System.IO.Directory]::Delete($testRoot, $true) }
}
