$ErrorActionPreference = 'Stop'

$publisherPath = Join-Path $PSScriptRoot '..\scripts\publish.ps1'
$publisherText = Get-Content -LiteralPath $publisherPath -Raw
if ($publisherText -match 'DevTools|PublisherCache|release'',\s*''download''|git\s+ls-remote|git\s+fetch|git\s+push') {
    throw 'Local publisher contains an external Publisher dependency or a prohibited network Git command.'
}
if ($publisherText -notmatch 'repos/\$Repository/git/ref/heads/\$Branch') {
    throw 'Local publisher does not query the remote branch SHA through the GitHub API.'
}

. $publisherPath

function Assert-Equal {
    param(
        [Parameter(Mandatory)] [string]$Expected,
        [Parameter(Mandatory)] [string]$Actual,
        [Parameter(Mandatory)] [string]$Name
    )

    if ($Expected -ne $Actual) {
        throw "$Name failed. Expected '$Expected', got '$Actual'."
    }
}

$cases = @(
    [pscustomobject]@{ Name = 'first development release starts at dev.1'; Channel = 'Development'; Tags = @(); Expected = 'v0.1.0-dev.1' },
    [pscustomobject]@{ Name = 'development sequence increments within the pending production version'; Channel = 'Development'; Tags = @('v0.1.0-dev.1', 'v0.1.0-dev.2', 'unrelated-tag'); Expected = 'v0.1.0-dev.3' },
    [pscustomobject]@{ Name = 'first production release is not blocked by development tags'; Channel = 'Production'; Tags = @('v0.1.0-dev.4'); Expected = 'v0.1.0' },
    [pscustomobject]@{ Name = 'production increments the highest stable patch'; Channel = 'Production'; Tags = @('v0.1.0', 'v0.1.7', 'v0.1.8-dev.2', 'v0.0.99'); Expected = 'v0.1.8' },
    [pscustomobject]@{ Name = 'development moves to the next patch after a production release'; Channel = 'Development'; Tags = @('v0.1.0', 'v0.1.0-dev.9'); Expected = 'v0.1.1-dev.1' },
    [pscustomobject]@{ Name = 'development continues an already opened next patch'; Channel = 'Development'; Tags = @('v0.1.0', 'v0.1.1-dev.2'); Expected = 'v0.1.1-dev.3' },
    [pscustomobject]@{ Name = 'version comparison supports a higher minor version'; Channel = 'Production'; Tags = @('v0.9.99', 'v0.10.0'); Expected = 'v0.10.1' }
)

foreach ($case in $cases) {
    Assert-Equal -Expected $case.Expected -Actual (Get-NextReleaseVersion -Channel $case.Channel -Tags $case.Tags) -Name $case.Name
}

Write-Host "Passed $($cases.Count) release-version and local-publisher checks."
