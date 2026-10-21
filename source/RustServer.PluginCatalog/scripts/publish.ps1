[CmdletBinding()]
param(
    [ValidateSet('Development', 'Production')]
    [string]$Channel,

    [string]$PublishDirectory,

    [string]$Repository = 'sysdba925/RustServer.PluginCatalog',

    [string]$RequiredBranch,

    [switch]$DryRun,

    [string[]]$KnownTags,

    [string]$ArchiveDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-VersionComponents {
    param(
        [Parameter(Mandatory)]
        [int]$Major,

        [Parameter(Mandatory)]
        [int]$Minor,

        [Parameter(Mandatory)]
        [int]$Patch
    )

    [pscustomobject]@{
        Major = $Major
        Minor = $Minor
        Patch = $Patch
    }
}

function ConvertFrom-ReleaseTag {
    param(
        [Parameter(Mandatory)]
        [string]$Tag
    )

    $stableMatch = [regex]::Match(
        $Tag,
        '^v(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)$')

    if ($stableMatch.Success) {
        return [pscustomobject]@{
            Channel = 'Production'
            Major = [int]$stableMatch.Groups['major'].Value
            Minor = [int]$stableMatch.Groups['minor'].Value
            Patch = [int]$stableMatch.Groups['patch'].Value
            Sequence = $null
        }
    }

    $developmentMatch = [regex]::Match(
        $Tag,
        '^v(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)-dev\.(?<sequence>[1-9]\d*)$')

    if ($developmentMatch.Success) {
        return [pscustomobject]@{
            Channel = 'Development'
            Major = [int]$developmentMatch.Groups['major'].Value
            Minor = [int]$developmentMatch.Groups['minor'].Value
            Patch = [int]$developmentMatch.Groups['patch'].Value
            Sequence = [int]$developmentMatch.Groups['sequence'].Value
        }
    }

    return $null
}

function Compare-VersionComponents {
    param(
        [Parameter(Mandatory)]
        [psobject]$Left,

        [Parameter(Mandatory)]
        [psobject]$Right
    )

    foreach ($part in @('Major', 'Minor', 'Patch')) {
        if ([int]$Left.$part -lt [int]$Right.$part) {
            return -1
        }

        if ([int]$Left.$part -gt [int]$Right.$part) {
            return 1
        }
    }

    return 0
}

function Get-HighestVersionComponents {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$Versions
    )

    $highest = $null

    foreach ($version in $Versions) {
        if ($null -eq $highest -or (Compare-VersionComponents -Left $version -Right $highest) -gt 0) {
            $highest = $version
        }
    }

    return $highest
}

function Get-NextProductionVersionComponents {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$Tags
    )

    $productionVersions = @(
        foreach ($tag in $Tags) {
            $version = ConvertFrom-ReleaseTag -Tag $tag
            if ($null -ne $version -and $version.Channel -eq 'Production') {
                $version
            }
        }
    )

    $latest = Get-HighestVersionComponents -Versions $productionVersions
    if ($null -eq $latest) {
        return New-VersionComponents -Major 0 -Minor 1 -Patch 0
    }

    if ($latest.Patch -eq [int]::MaxValue) {
        throw "Cannot increment patch version for tag v$($latest.Major).$($latest.Minor).$($latest.Patch)."
    }

    return New-VersionComponents -Major $latest.Major -Minor $latest.Minor -Patch ($latest.Patch + 1)
}

function Format-StableVersion {
    param(
        [Parameter(Mandatory)]
        [psobject]$Version
    )

    return "v$($Version.Major).$($Version.Minor).$($Version.Patch)"
}

function Get-NextReleaseVersion {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Development', 'Production')]
        [string]$Channel,

        [AllowEmptyCollection()]
        [string[]]$Tags = @()
    )

    $normalizedTags = @($Tags | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $nextProductionVersion = Get-NextProductionVersionComponents -Tags $normalizedTags

    if ($Channel -eq 'Production') {
        $nextVersion = Format-StableVersion -Version $nextProductionVersion
        if ($normalizedTags -contains $nextVersion) {
            throw "Refusing to reuse existing tag '$nextVersion'."
        }

        return $nextVersion
    }

    $developmentVersions = @(
        foreach ($tag in $normalizedTags) {
            $version = ConvertFrom-ReleaseTag -Tag $tag
            if ($null -ne $version -and $version.Channel -eq 'Development') {
                $version
            }
        }
    )

    $highestDevelopmentVersion = Get-HighestVersionComponents -Versions $developmentVersions
    $developmentBase = $nextProductionVersion
    if ($null -ne $highestDevelopmentVersion -and
        (Compare-VersionComponents -Left $highestDevelopmentVersion -Right $developmentBase) -gt 0) {
        $developmentBase = New-VersionComponents `
            -Major $highestDevelopmentVersion.Major `
            -Minor $highestDevelopmentVersion.Minor `
            -Patch $highestDevelopmentVersion.Patch
    }

    $sequence = 0
    foreach ($version in $developmentVersions) {
        if ((Compare-VersionComponents -Left $version -Right $developmentBase) -eq 0 -and
            $version.Sequence -gt $sequence) {
            $sequence = $version.Sequence
        }
    }

    if ($sequence -eq [int]::MaxValue) {
        throw "Cannot increment development sequence for $(Format-StableVersion -Version $developmentBase)."
    }

    $nextVersion = "$(Format-StableVersion -Version $developmentBase)-dev.$($sequence + 1)"
    if ($normalizedTags -contains $nextVersion) {
        throw "Refusing to reuse existing tag '$nextVersion'."
    }

    return $nextVersion
}

function Invoke-NativeCommand {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [string[]]$Arguments = @()
    )

    if ($null -eq (Get-Command $FilePath -ErrorAction SilentlyContinue)) {
        throw "Required command '$FilePath' was not found on PATH."
    }

    $output = @(& $FilePath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $outputText = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine

    if ($exitCode -ne 0) {
        throw "Command '$FilePath $($Arguments -join ' ')' failed with exit code $exitCode.$([Environment]::NewLine)$outputText"
    }

    return $outputText.Trim()
}

function Get-GitHubCliPath {
    [OutputType([string])]

    $fromPath = Get-Command 'gh' -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        return $fromPath.Source
    }

    $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $standardPath = Join-Path -Path $programFiles -ChildPath 'GitHub CLI\gh.exe'
    if (Test-Path -LiteralPath $standardPath -PathType Leaf) {
        return $standardPath
    }

    throw "GitHub CLI ('gh') was not found on PATH or at '$standardPath'. Install GitHub CLI and authenticate with 'gh auth login'."
}

function Assert-GitHubAuthentication {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [string]$GitHubCliPath,

        [Parameter(Mandatory)]
        [string]$Repository,

        [Parameter(Mandatory)]
        [string]$ExpectedGitHubAccount
    )

    try {
        Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments @('auth', 'status', '--hostname', 'github.com') | Out-Null
    }
    catch {
        throw "GitHub CLI is not authenticated for github.com. Run 'gh auth login --hostname github.com' and grant the repo scope. $($_.Exception.Message)"
    }

    $account = Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments @('api', 'user', '--jq', '.login')
    if ([string]::IsNullOrWhiteSpace($account)) {
        throw 'GitHub CLI did not return an active GitHub account.'
    }

    if (-not $account.Equals($ExpectedGitHubAccount, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "GitHub CLI is authenticated as '$account', but publishing requires '$ExpectedGitHubAccount'."
    }

    $resolvedRepository = Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments @('api', "repos/$Repository", '--jq', '.full_name')
    if (-not $resolvedRepository.Equals($Repository, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "GitHub API resolved '$resolvedRepository', not expected repository '$Repository'."
    }

    return [pscustomobject]@{
        Account = $account
        Repository = $resolvedRepository
    }
}

function Get-GitHubRemoteHead {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]
        [string]$GitHubCliPath,

        [Parameter(Mandatory)]
        [string]$Repository,

        [Parameter(Mandatory)]
        [string]$Branch
    )

    $sha = Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments @('api', "repos/$Repository/git/ref/heads/$Branch", '--jq', '.object.sha')
    if ($sha -notmatch '^[0-9a-fA-F]{40,64}$') {
        throw "GitHub API returned an invalid commit SHA for '$Repository/$Branch'."
    }

    return $sha
}

function ConvertFrom-GitHubRemoteUrl {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]
        [string]$Url
    )

    $normalizedUrl = $Url.Trim().TrimEnd('/') -replace '\.git$', ''
    $match = [regex]::Match(
        $normalizedUrl,
        '^(?:https?://|ssh://git@|git@)github\.com[:/](?<owner>[A-Za-z0-9_.-]+)/(?<repository>[A-Za-z0-9_.-]+)$')

    if (-not $match.Success) {
        throw "Origin remote '$Url' is not a GitHub repository URL."
    }

    return "$($match.Groups['owner'].Value)/$($match.Groups['repository'].Value)"
}

function Test-GitPreconditions {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [string]$Repository,

        [Parameter(Mandatory)]
        [string]$GitHubCliPath,

        [string]$RequiredBranch
    )

    $insideRepository = Invoke-NativeCommand -FilePath 'git' -Arguments @('rev-parse', '--is-inside-work-tree')
    if ($insideRepository -ne 'true') {
        throw 'Publish must be run from a Git working tree.'
    }

    $status = Invoke-NativeCommand -FilePath 'git' -Arguments @('status', '--porcelain')
    if (-not [string]::IsNullOrWhiteSpace($status)) {
        throw 'Publish requires a clean Git working tree.'
    }

    $branch = Invoke-NativeCommand -FilePath 'git' -Arguments @('branch', '--show-current')
    if ([string]::IsNullOrWhiteSpace($branch)) {
        throw 'Publish does not support a detached HEAD.'
    }

    if (-not [string]::IsNullOrWhiteSpace($RequiredBranch) -and $branch -ne $RequiredBranch) {
        throw "Publish profile requires branch '$RequiredBranch', but the current branch is '$branch'."
    }

    $originUrl = Invoke-NativeCommand -FilePath 'git' -Arguments @('remote', 'get-url', 'origin')
    $originRepository = ConvertFrom-GitHubRemoteUrl -Url $originUrl
    if (-not $originRepository.Equals($Repository, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Origin remote points to '$originRepository', not expected repository '$Repository'."
    }

    $remoteName = Invoke-NativeCommand -FilePath 'git' -Arguments @('config', '--get', "branch.$branch.remote")
    $mergeReference = Invoke-NativeCommand -FilePath 'git' -Arguments @('config', '--get', "branch.$branch.merge")
    if ([string]::IsNullOrWhiteSpace($remoteName) -or
        [string]::IsNullOrWhiteSpace($mergeReference) -or
        $remoteName -ne 'origin' -or
        $mergeReference -ne "refs/heads/$branch") {
        throw "Branch '$branch' must track 'origin/$branch' before publishing."
    }

    $headCommit = Invoke-NativeCommand -FilePath 'git' -Arguments @('rev-parse', 'HEAD')
    $remoteCommit = Get-GitHubRemoteHead -GitHubCliPath $GitHubCliPath -Repository $Repository -Branch $branch
    if (-not $headCommit.Equals($remoteCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "HEAD ($headCommit) is not the current commit of origin/$branch ($remoteCommit). Push and synchronize the branch before publishing."
    }

    return [pscustomobject]@{
        Branch = $branch
        Commit = $headCommit
    }
}

function Get-GitHubTags {
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)]
        [string]$Repository,

        [Parameter(Mandatory)]
        [string]$GitHubCliPath
    )

    $tagOutput = Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments @(
        'api',
        '--paginate',
        "repos/$Repository/tags?per_page=100",
        '--jq',
        '.[].name'
    )

    if ([string]::IsNullOrWhiteSpace($tagOutput)) {
        return @()
    }

    return @(
        $tagOutput -split "`r?`n" |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

function New-PublishArchive {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [string]$PublishDirectory,

        [Parameter(Mandatory)]
        [string]$Version,

        [string]$ArchiveDirectory
    )

    if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
        throw "Publish directory '$PublishDirectory' does not exist."
    }

    $resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
    if (@(Get-ChildItem -LiteralPath $resolvedPublishDirectory -Force).Count -eq 0) {
        throw "Publish directory '$resolvedPublishDirectory' is empty."
    }

    $requiredFiles = @(
        'RustServer.PluginCatalog.Api.dll',
        'RustServer.PluginCatalog.Api.deps.json',
        'RustServer.PluginCatalog.Api.runtimeconfig.json',
        'provisioning/provision.sh',
        'provisioning/host/plugin-catalog.env',
        'provisioning/host/rustserver-plugin-catalog.service',
        'provisioning/host/rustserver-plugin-catalog.nginx.conf',
        'provisioning/database/postgresql/initial-schema-current.sql',
        'provisioning/database/postgresql/validate-existing-schema.sql'
    )
    foreach ($file in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $resolvedPublishDirectory $file) -PathType Leaf)) {
            throw "Publish output is missing '$file'."
        }
    }

    . (Join-Path $PSScriptRoot 'assert-shell-line-endings.ps1')
    Assert-ShellLineEndings -Files @(Get-ChildItem -LiteralPath $resolvedPublishDirectory -Recurse -File)

    if ([string]::IsNullOrWhiteSpace($ArchiveDirectory)) {
        $repositoryRoot = Invoke-NativeCommand -FilePath 'git' -Arguments @('rev-parse', '--show-toplevel')
        $ArchiveDirectory = Join-Path -Path $repositoryRoot -ChildPath 'artifacts/releases'
    }

    New-Item -ItemType Directory -Path $ArchiveDirectory -Force | Out-Null
    $resolvedArchiveDirectory = (Resolve-Path -LiteralPath $ArchiveDirectory).Path
    $archiveName = "RustServer.PluginCatalog-$Version-linux-x64.tar.gz"
    $archivePath = Join-Path -Path $resolvedArchiveDirectory -ChildPath $archiveName
    $checksumPath = "$archivePath.sha256"

    if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $checksumPath)) {
        throw "Refusing to overwrite existing release artifact '$archivePath'."
    }

    Invoke-NativeCommand -FilePath 'tar.exe' -Arguments @('-czf', $archivePath, '-C', $resolvedPublishDirectory, '.') | Out-Null

    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        throw "Archive '$archivePath' was not created."
    }

    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumContent = "$hash  $archiveName$([Environment]::NewLine)"
    [System.IO.File]::WriteAllText($checksumPath, $checksumContent, [System.Text.Encoding]::ASCII)

    return [pscustomobject]@{
        ArchivePath = $archivePath
        ChecksumPath = $checksumPath
        Sha256 = $hash
    }
}

function Confirm-ProductionPublication {
    param(
        [Parameter(Mandatory)]
        [string]$Version,

        [Parameter(Mandatory)]
        [string]$Repository,

        [Parameter(Mandatory)]
        [string]$Commit
    )

    if (-not [Environment]::UserInteractive) {
        throw 'Production publishing requires interactive user confirmation.'
    }

    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        $message = "Create production release $Version in $Repository for commit $Commit?"
        $answer = [System.Windows.Forms.MessageBox]::Show(
            $message,
            'Confirm GitHub production release',
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning,
            [System.Windows.Forms.MessageBoxDefaultButton]::Button2)
    }
    catch {
        throw "Production publishing requires confirmation, but the confirmation dialog could not be shown. $($_.Exception.Message)"
    }

    if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
        throw 'Production release was cancelled by the user.'
    }
}

function Publish-GitHubRelease {
    param(
        [Parameter(Mandatory)]
        [string]$Repository,

        [Parameter(Mandatory)]
        [ValidateSet('Development', 'Production')]
        [string]$Channel,

        [Parameter(Mandatory)]
        [string]$Version,

        [Parameter(Mandatory)]
        [string]$Commit,

        [Parameter(Mandatory)]
        [psobject]$Artifact
    )

    $arguments = @(
        'release',
        'create',
        $Version,
        $Artifact.ArchivePath,
        $Artifact.ChecksumPath,
        '--repo',
        $Repository,
        '--target',
        $Commit,
        '--title',
        $Version,
        '--notes',
        "Automated $Channel release from commit $Commit."
    )

    if ($Channel -eq 'Development') {
        $arguments += '--prerelease'
    }

    Invoke-NativeCommand -FilePath 'gh' -Arguments $arguments | Out-Null
}

function Invoke-Publish {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Development', 'Production')]
        [string]$Channel,

        [Parameter(Mandatory)]
        [string]$PublishDirectory,

        [Parameter(Mandatory)]
        [string]$Repository,

        [string]$RequiredBranch,

        [switch]$DryRun,

        [string[]]$KnownTags,

        [string]$ArchiveDirectory
    )

    $gitHubCliPath = Get-GitHubCliPath
    $authentication = Assert-GitHubAuthentication -GitHubCliPath $gitHubCliPath -Repository $Repository -ExpectedGitHubAccount 'sysdba925'
    $gitState = Test-GitPreconditions -GitHubCliPath $gitHubCliPath -Repository $Repository -RequiredBranch $RequiredBranch
    $tags = if ($PSBoundParameters.ContainsKey('KnownTags')) {
        @($KnownTags)
    }
    else {
        Get-GitHubTags -GitHubCliPath $gitHubCliPath -Repository $Repository
    }

    $version = Get-NextReleaseVersion -Channel $Channel -Tags $tags
    $artifact = New-PublishArchive `
        -PublishDirectory $PublishDirectory `
        -Version $version `
        -ArchiveDirectory $ArchiveDirectory

    Write-Host "Prepared $version ($($artifact.Sha256)) from commit $($gitState.Commit) as GitHub account $($authentication.Account)."
    Write-Host "Archive: $($artifact.ArchivePath)"
    Write-Host "Checksum: $($artifact.ChecksumPath)"

    if ($DryRun) {
        Write-Host "Dry run: no GitHub Release was created and no existing release was deleted."
        return
    }

    if ($Channel -eq 'Production') {
        Confirm-ProductionPublication -Version $version -Repository $Repository -Commit $gitState.Commit
    }

    Publish-GitHubRelease `
        -Repository $Repository `
        -Channel $Channel `
        -Version $version `
        -Commit $gitState.Commit `
        -Artifact $artifact

    Write-Host "Published $version to $Repository."
}

# Dot-sourcing is used by the PowerShell tests to exercise the versioning functions
# without running a publication.
if ($MyInvocation.InvocationName -eq '.') {
    return
}

try {
    if ([string]::IsNullOrWhiteSpace($Channel) -or [string]::IsNullOrWhiteSpace($PublishDirectory)) {
        throw 'Channel and PublishDirectory are required.'
    }

    $invokeParameters = @{
        Channel = $Channel
        PublishDirectory = $PublishDirectory
        Repository = $Repository
        RequiredBranch = $RequiredBranch
        DryRun = $DryRun
        ArchiveDirectory = $ArchiveDirectory
    }
    if ($PSBoundParameters.ContainsKey('KnownTags')) {
        $invokeParameters.KnownTags = $KnownTags
    }

    Invoke-Publish @invokeParameters
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
