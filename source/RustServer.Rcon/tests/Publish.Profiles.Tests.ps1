$ErrorActionPreference = 'Stop'

$profilesDirectory = Join-Path $PSScriptRoot '..\src\RustServer.Rcon.Grpc\Properties\PublishProfiles'
$profiles = @(
    [pscustomobject]@{ FileName = 'Development.pubxml'; TargetName = 'PublishDevelopmentGitHubRelease'; Channel = 'Development' },
    [pscustomobject]@{ FileName = 'Production.pubxml'; TargetName = 'PublishProductionGitHubRelease'; Channel = 'Production' }
)

foreach ($profile in $profiles) {
    $profilePath = Join-Path $profilesDirectory $profile.FileName
    [xml]$profileXml = Get-Content -LiteralPath $profilePath -Raw
    $target = $profileXml.SelectSingleNode("/Project/Target[@Name='$($profile.TargetName)']")
    if ($null -eq $target) {
        throw "$($profile.FileName) does not define target '$($profile.TargetName)'."
    }

    if ($target.GetAttribute('AfterTargets') -ne 'AfterPublish') {
        throw "$($profile.FileName) must hook the Visual Studio publish lifecycle through AfterPublish."
    }

    $publishIntermediateOutputPathError = $target.SelectSingleNode('./Error')
    $expectedPublishIntermediateOutputPathCondition = "'`$(PublishIntermediateOutputPath)' == ''"
    if ($null -eq $publishIntermediateOutputPathError -or $publishIntermediateOutputPathError.GetAttribute('Condition') -ne $expectedPublishIntermediateOutputPathCondition) {
        throw "$($profile.FileName) must reject a missing PublishIntermediateOutputPath before archiving."
    }

    $publishDirectory = $target.SelectSingleNode('./PropertyGroup/GitHubReleasePublishDirectory')
    if ($null -eq $publishDirectory -or $publishDirectory.InnerText -notmatch 'PublishIntermediateOutputPath') {
        throw "$($profile.FileName) does not pass the published service directory to the release script."
    }

    $exec = $target.SelectSingleNode('./Exec')
    if ($null -eq $exec) {
        throw "$($profile.FileName) does not execute the release publisher."
    }

    $command = $exec.GetAttribute('Command')
    foreach ($requiredArgument in @(
        'powershell.exe',
        'scripts\publish.ps1',
        "-Channel $($profile.Channel)",
        '-PublishDirectory',
        '-Repository "sysdba925/RustServer.Rcon"',
        '-RequiredBranch develop')) {
        if ($command -notlike "*$requiredArgument*") {
            throw "$($profile.FileName) does not pass required publisher argument '$requiredArgument'."
        }
    }

    if ($exec.HasAttribute('IgnoreExitCode') -and [System.Convert]::ToBoolean($exec.GetAttribute('IgnoreExitCode'))) {
        throw "$($profile.FileName) ignores publisher failures, which would report a false publish success."
    }

    if ($exec.HasAttribute('ContinueOnError') -and -not [string]::IsNullOrWhiteSpace($exec.GetAttribute('ContinueOnError'))) {
        throw "$($profile.FileName) continues after publisher failures, which would report a false publish success."
    }
}

Write-Host "Passed AfterPublish hook, service publish directory, publisher arguments, and failure-propagation checks for $($profiles.Count) publish profiles."
