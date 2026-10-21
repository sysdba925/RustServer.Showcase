[CmdletBinding()]
param(
    [ValidateSet('Development', 'Production')]
    [string]$Channel,
    [string]$PublishDirectory,
    [string]$Repository = 'sysdba925/RustServer.Snapshots',
    [string]$RequiredBranch,
    [switch]$DryRun,
    [string[]]$KnownTags,
    [string]$ArchiveDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:PackageName = 'RustServer.Snapshots'
$script:RuntimeIdentifier = 'linux-x64'
$script:ExpectedGitHubAccount = 'sysdba925'
$script:RequiredPublishFiles = @(
    'RustServer.Snapshots.Api.dll',
    'RustServer.Snapshots.Api.deps.json',
    'RustServer.Snapshots.Api.runtimeconfig.json'
)

function Invoke-NativeCommand {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$FailureHint
    )

    if ($null -eq (Get-Command $FilePath -ErrorAction SilentlyContinue)) {
        throw "Required command '$FilePath' was not found. $FailureHint".Trim()
    }

    $output = @(& $FilePath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $outputText = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    if ($exitCode -ne 0) {
        $suffix = if ([string]::IsNullOrWhiteSpace($FailureHint)) { '' } else { " $FailureHint" }
        throw "Command '$FilePath $($Arguments -join ' ')' failed with exit code $exitCode.$suffix$([Environment]::NewLine)$outputText"
    }

    return $outputText.Trim()
}

function Get-GitHubCliPath {
    [OutputType([string])]

    $fromPath = Get-Command 'gh' -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) { return $fromPath.Source }

    $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $standardPath = Join-Path -Path $programFiles -ChildPath 'GitHub CLI\gh.exe'
    if (Test-Path -LiteralPath $standardPath -PathType Leaf) { return $standardPath }

    throw "GitHub CLI ('gh') was not found on PATH or at '$standardPath'. Install GitHub CLI and authenticate with 'gh auth login'."
}

function Assert-GitHubAuthentication {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] [string]$GitHubCliPath,
        [Parameter(Mandatory)] [string]$Repository,
        [Parameter(Mandatory)] [string]$ExpectedGitHubAccount
    )

    try {
        Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments @('auth', 'status', '--hostname', 'github.com') -FailureHint "Run 'gh auth login --hostname github.com' and grant the repo scope." | Out-Null
    }
    catch {
        throw "GitHub CLI is not authenticated for github.com. Run 'gh auth login --hostname github.com' and grant the repo scope. $($_.Exception.Message)"
    }

    $account = Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments @('api', 'user', '--jq', '.login') -FailureHint 'Unable to determine the active GitHub account.'
    if ([string]::IsNullOrWhiteSpace($account)) { throw 'GitHub CLI did not return an active GitHub account.' }
    if (-not $account.Equals($ExpectedGitHubAccount, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "GitHub CLI is authenticated as '$account', but publishing requires '$ExpectedGitHubAccount'."
    }

    $resolvedRepository = Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments @('api', "repos/$Repository", '--jq', '.full_name') -FailureHint "The active account '$account' cannot access '$Repository'."
    if (-not $resolvedRepository.Equals($Repository, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "GitHub API resolved '$resolvedRepository', not expected repository '$Repository'."
    }

    return [pscustomobject]@{ Account = $account; Repository = $resolvedRepository }
}

function Get-GitHubRemoteHead {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string]$GitHubCliPath,
        [Parameter(Mandatory)] [string]$Repository,
        [Parameter(Mandatory)] [string]$Branch
    )

    $sha = Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments @('api', "repos/$Repository/git/ref/heads/$Branch", '--jq', '.object.sha') -FailureHint "Unable to resolve GitHub branch '$Branch' for '$Repository'."
    if ($sha -notmatch '^[0-9a-fA-F]{40,64}$') {
        throw "GitHub API returned an invalid commit SHA for '$Repository/$Branch'."
    }

    return $sha
}

function ConvertFrom-GitHubRemoteUrl {
    [OutputType([string])]
    param([Parameter(Mandatory)] [string]$Url)

    $normalizedUrl = $Url.Trim().TrimEnd('/') -replace '\.git$', ''
    $match = [regex]::Match($normalizedUrl, '^(?:https?://|ssh://git@|git@)github\.com[:/](?<owner>[A-Za-z0-9_.-]+)/(?<repository>[A-Za-z0-9_.-]+)$')
    if (-not $match.Success) { throw "Origin remote '$Url' is not a GitHub repository URL." }

    return "$($match.Groups['owner'].Value)/$($match.Groups['repository'].Value)"
}

function Test-GitPreconditions {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] [string]$Repository,
        [Parameter(Mandatory)] [string]$GitHubCliPath,
        [string]$RequiredBranch
    )

    $insideRepository = Invoke-NativeCommand -FilePath 'git' -Arguments @('rev-parse', '--is-inside-work-tree')
    if ($insideRepository -ne 'true') { throw 'Publish must be run from a Git working tree.' }

    $status = Invoke-NativeCommand -FilePath 'git' -Arguments @('status', '--porcelain')
    if (-not [string]::IsNullOrWhiteSpace($status)) { throw 'Publish requires a clean Git working tree.' }

    $branch = Invoke-NativeCommand -FilePath 'git' -Arguments @('branch', '--show-current')
    if ([string]::IsNullOrWhiteSpace($branch)) { throw 'Publish does not support a detached HEAD.' }
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
    if ([string]::IsNullOrWhiteSpace($remoteName) -or [string]::IsNullOrWhiteSpace($mergeReference) -or $remoteName -eq '.' -or $remoteName -ne 'origin' -or $mergeReference -ne "refs/heads/$branch") {
        throw "Branch '$branch' must track 'origin/$branch' before publishing."
    }

    $headCommit = Invoke-NativeCommand -FilePath 'git' -Arguments @('rev-parse', 'HEAD')
    $remoteCommit = Get-GitHubRemoteHead -GitHubCliPath $GitHubCliPath -Repository $Repository -Branch $branch
    if (-not $headCommit.Equals($remoteCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "HEAD ($headCommit) is not the current commit of origin/$branch ($remoteCommit). Push and synchronize the branch before publishing."
    }

    return [pscustomobject]@{ Branch = $branch; Commit = $headCommit }
}

function Get-GitHubTags {
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)] [string]$GitHubCliPath,
        [Parameter(Mandatory)] [string]$Repository
    )

    $tagOutput = Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments @('api', '--paginate', "repos/$Repository/tags?per_page=100", '--jq', '.[].name') -FailureHint "Unable to read tags from '$Repository'."
    if ([string]::IsNullOrWhiteSpace($tagOutput)) { return @() }

    return @($tagOutput -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function New-VersionComponents {
    param(
        [Parameter(Mandatory)] [int]$Major,
        [Parameter(Mandatory)] [int]$Minor,
        [Parameter(Mandatory)] [int]$Patch
    )

    return [pscustomobject]@{ Major = $Major; Minor = $Minor; Patch = $Patch }
}

function ConvertFrom-ReleaseTag {
    [OutputType([pscustomobject])]
    param([Parameter(Mandatory)] [string]$Tag)

    $stableMatch = [regex]::Match($Tag, '^v(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)$')
    if ($stableMatch.Success) {
        return [pscustomobject]@{ Channel = 'Production'; Major = [int]$stableMatch.Groups['major'].Value; Minor = [int]$stableMatch.Groups['minor'].Value; Patch = [int]$stableMatch.Groups['patch'].Value; Sequence = $null }
    }

    $developmentMatch = [regex]::Match($Tag, '^v(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)-dev\.(?<sequence>[1-9]\d*)$')
    if ($developmentMatch.Success) {
        return [pscustomobject]@{ Channel = 'Development'; Major = [int]$developmentMatch.Groups['major'].Value; Minor = [int]$developmentMatch.Groups['minor'].Value; Patch = [int]$developmentMatch.Groups['patch'].Value; Sequence = [int]$developmentMatch.Groups['sequence'].Value }
    }

    return $null
}

function Compare-VersionComponents {
    [OutputType([int])]
    param(
        [Parameter(Mandatory)] [psobject]$Left,
        [Parameter(Mandatory)] [psobject]$Right
    )

    foreach ($part in @('Major', 'Minor', 'Patch')) {
        if ([int]$Left.$part -lt [int]$Right.$part) { return -1 }
        if ([int]$Left.$part -gt [int]$Right.$part) { return 1 }
    }

    return 0
}

function Get-HighestVersionComponents {
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]]$Versions)

    $highest = $null
    foreach ($version in $Versions) {
        if ($null -eq $highest -or (Compare-VersionComponents -Left $version -Right $highest) -gt 0) {
            $highest = $version
        }
    }

    return $highest
}

function Get-NextProductionVersionComponents {
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]]$Tags)

    $productionVersions = @(
        foreach ($tag in $Tags) {
            $version = ConvertFrom-ReleaseTag -Tag $tag
            if ($null -ne $version -and $version.Channel -eq 'Production') { $version }
        }
    )

    $latest = Get-HighestVersionComponents -Versions $productionVersions
    if ($null -eq $latest) { return New-VersionComponents -Major 0 -Minor 1 -Patch 0 }
    if ($latest.Patch -eq [int]::MaxValue) {
        throw "Cannot increment patch version for tag v$($latest.Major).$($latest.Minor).$($latest.Patch)."
    }

    return New-VersionComponents -Major $latest.Major -Minor $latest.Minor -Patch ($latest.Patch + 1)
}

function Format-StableVersion {
    [OutputType([string])]
    param([Parameter(Mandatory)] [psobject]$Version)

    return "v$($Version.Major).$($Version.Minor).$($Version.Patch)"
}

function Get-NextReleaseVersion {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [ValidateSet('Development', 'Production')] [string]$Channel,
        [AllowEmptyCollection()] [string[]]$Tags = @()
    )

    $normalizedTags = @($Tags | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $nextProductionVersion = Get-NextProductionVersionComponents -Tags $normalizedTags
    if ($Channel -eq 'Production') {
        $nextVersion = Format-StableVersion -Version $nextProductionVersion
        if ($normalizedTags -contains $nextVersion) { throw "Refusing to reuse existing tag '$nextVersion'." }
        return $nextVersion
    }

    $developmentVersions = @(
        foreach ($tag in $normalizedTags) {
            $version = ConvertFrom-ReleaseTag -Tag $tag
            if ($null -ne $version -and $version.Channel -eq 'Development') { $version }
        }
    )

    $highestDevelopmentVersion = Get-HighestVersionComponents -Versions $developmentVersions
    $developmentBase = $nextProductionVersion
    if ($null -ne $highestDevelopmentVersion -and (Compare-VersionComponents -Left $highestDevelopmentVersion -Right $developmentBase) -gt 0) {
        $developmentBase = New-VersionComponents -Major $highestDevelopmentVersion.Major -Minor $highestDevelopmentVersion.Minor -Patch $highestDevelopmentVersion.Patch
    }

    $sequence = 0
    foreach ($version in $developmentVersions) {
        if ((Compare-VersionComponents -Left $version -Right $developmentBase) -eq 0 -and $version.Sequence -gt $sequence) {
            $sequence = $version.Sequence
        }
    }

    if ($sequence -eq [int]::MaxValue) {
        throw "Cannot increment development sequence for $(Format-StableVersion -Version $developmentBase)."
    }

    $nextVersion = "$(Format-StableVersion -Version $developmentBase)-dev.$($sequence + 1)"
    if ($normalizedTags -contains $nextVersion) { throw "Refusing to reuse existing tag '$nextVersion'." }
    return $nextVersion
}

function Assert-SafePublishFile {
    param(
        [Parameter(Mandatory)] [System.IO.FileInfo]$File,
        [Parameter(Mandatory)] [string]$RelativePath
    )

    if ($RelativePath -match '(?i)(^|[\\/])\.env(?:\..+)?$' -or
        $RelativePath -match '(?i)\.pubxml\.user$' -or
        $RelativePath -match '(?i)(^|[\\/])(?:secrets?|credentials?)(?:[._-].*)?$' -or
        $RelativePath -match '(?i)(^|[\\/])appsettings\.(?:development|local)\.json$') {
        throw "Publish input contains prohibited local or secret file '$RelativePath'."
    }

    if ($File.Extension.Equals('.json', [System.StringComparison]::OrdinalIgnoreCase)) {
        $content = [System.IO.File]::ReadAllText($File.FullName)
        if ($content -match '(?i)"(?:password|token|secret|credential|api[_-]?key)"\s*:\s*"(?!\s*(?:\$\{[^}]+\}|"))') {
            throw "Publish input contains a configured secret in '$RelativePath'."
        }
    }
}

function Test-SnapshotsPublishDirectory {
    [OutputType([pscustomobject])]
    param([Parameter(Mandatory)] [string]$PublishDirectory)

    if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
        throw "Publish directory '$PublishDirectory' does not exist."
    }

    $resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
    foreach ($requiredFile in $script:RequiredPublishFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $resolvedPublishDirectory $requiredFile) -PathType Leaf)) {
            throw "Publish directory '$resolvedPublishDirectory' does not contain required deployable file '$requiredFile'."
        }
    }

    $publishedFiles = @(Get-ChildItem -LiteralPath $resolvedPublishDirectory -File -Recurse -Force)
    if ($publishedFiles.Count -eq 0) { throw "Publish directory '$resolvedPublishDirectory' is empty." }
    foreach ($file in $publishedFiles) {
        $relativePath = $file.FullName.Substring($resolvedPublishDirectory.Length).TrimStart([char[]]'\\/')
        Assert-SafePublishFile -File $file -RelativePath $relativePath
    }

    return [pscustomobject]@{ Directory = $resolvedPublishDirectory; Files = $publishedFiles }
}

function ConvertTo-ArchiveRelativePath {
    [OutputType([string])]
    param([Parameter(Mandatory)] [string]$Path)

    $normalized = $Path.Replace('\', '/').Trim()
    while ($normalized.StartsWith('./')) { $normalized = $normalized.Substring(2) }
    return $normalized.TrimEnd('/')
}

function Test-PublishArchiveContents {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] [psobject]$PublishInput
    )

    $archiveListing = Invoke-NativeCommand -FilePath 'tar.exe' -Arguments @('-tzf', $ArchivePath) -FailureHint "Unable to verify archive '$ArchivePath'."
    $archivedFiles = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in ($archiveListing -split "`r?`n")) {
        $relativePath = ConvertTo-ArchiveRelativePath -Path $entry
        if (-not [string]::IsNullOrWhiteSpace($relativePath)) { $archivedFiles.Add($relativePath) | Out-Null }
    }

    foreach ($file in $PublishInput.Files) {
        $relativePath = $file.FullName.Substring($PublishInput.Directory.Length).TrimStart([char[]]'\\/').Replace('\', '/')
        if (-not $archivedFiles.Contains($relativePath)) {
            throw "Archive '$ArchivePath' does not contain published file '$relativePath'."
        }
    }

    foreach ($requiredFile in $script:RequiredPublishFiles) {
        if (-not $archivedFiles.Contains($requiredFile)) {
            throw "Archive '$ArchivePath' does not contain required deployable file '$requiredFile'."
        }
    }
}

function New-PublishArchive {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] [string]$PublishDirectory,
        [Parameter(Mandatory)] [string]$Version,
        [string]$ArchiveDirectory
    )

    $publishInput = Test-SnapshotsPublishDirectory -PublishDirectory $PublishDirectory
    if ([string]::IsNullOrWhiteSpace($ArchiveDirectory)) {
        $repositoryRoot = Invoke-NativeCommand -FilePath 'git' -Arguments @('rev-parse', '--show-toplevel')
        $ArchiveDirectory = Join-Path -Path $repositoryRoot -ChildPath 'artifacts/releases'
    }

    New-Item -ItemType Directory -Path $ArchiveDirectory -Force | Out-Null
    $resolvedArchiveDirectory = (Resolve-Path -LiteralPath $ArchiveDirectory).Path
    $archiveName = "$($script:PackageName)-$Version-$($script:RuntimeIdentifier).tar.gz"
    $archivePath = Join-Path -Path $resolvedArchiveDirectory -ChildPath $archiveName
    $checksumPath = "$archivePath.sha256"
    if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $checksumPath)) {
        throw "Refusing to overwrite existing release artifact '$archivePath'."
    }

    Invoke-NativeCommand -FilePath 'tar.exe' -Arguments @('-czf', $archivePath, '-C', $publishInput.Directory, '.') -FailureHint "Unable to create archive '$archivePath'." | Out-Null
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) { throw "Archive '$archivePath' was not created." }

    Test-PublishArchiveContents -ArchivePath $archivePath -PublishInput $publishInput
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText($checksumPath, "$hash  $archiveName$([Environment]::NewLine)", [System.Text.Encoding]::ASCII)
    return [pscustomobject]@{ ArchiveName = $archiveName; ArchivePath = $archivePath; ChecksumPath = $checksumPath; Sha256 = $hash }
}

function Confirm-ProductionPublication {
    param(
        [Parameter(Mandatory)] [string]$Version,
        [Parameter(Mandatory)] [string]$Repository,
        [Parameter(Mandatory)] [string]$Commit
    )

    if (-not [Environment]::UserInteractive) { throw 'Production publishing requires interactive user confirmation.' }
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "Create production release $Version in $Repository for commit $Commit?",
            'Confirm GitHub production release',
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning,
            [System.Windows.Forms.MessageBoxDefaultButton]::Button2)
    }
    catch {
        throw "Production publishing requires confirmation, but the confirmation dialog could not be shown. $($_.Exception.Message)"
    }

    if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) { throw 'Production release was cancelled by the user.' }
}

function Publish-GitHubRelease {
    param(
        [Parameter(Mandatory)] [string]$GitHubCliPath,
        [Parameter(Mandatory)] [string]$Repository,
        [Parameter(Mandatory)] [ValidateSet('Development', 'Production')] [string]$Channel,
        [Parameter(Mandatory)] [string]$Version,
        [Parameter(Mandatory)] [string]$Commit,
        [Parameter(Mandatory)] [psobject]$Artifact
    )

    $arguments = @('release', 'create', $Version, $Artifact.ArchivePath, $Artifact.ChecksumPath, '--repo', $Repository, '--target', $Commit, '--title', $Version, '--notes', "Automated $Channel release from commit $Commit.")
    if ($Channel -eq 'Development') { $arguments += '--prerelease' }
    Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments $arguments -FailureHint "Unable to create the GitHub release in '$Repository'." | Out-Null
}

function Remove-ObsoleteGitHubReleases {
    param(
        [Parameter(Mandatory)] [string]$GitHubCliPath,
        [Parameter(Mandatory)] [string]$Repository,
        [Parameter(Mandatory)] [ValidateSet('Development', 'Production')] [string]$Channel
    )

    $releaseJson = Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments @('api', '--paginate', '--slurp', "repos/$Repository/releases?per_page=100") -FailureHint "Unable to query releases from '$Repository'."
    $pages = ConvertFrom-Json -InputObject $releaseJson
    $releases = @(
        foreach ($page in @($pages)) {
            foreach ($release in @($page)) { $release }
        }
    )

    $channelReleases = @(
        foreach ($release in $releases) {
            if ($release.draft) { continue }
            $version = ConvertFrom-ReleaseTag -Tag $release.tag_name
            if ($null -eq $version -or $version.Channel -ne $Channel) { continue }
            if (($Channel -eq 'Development' -and -not $release.prerelease) -or ($Channel -eq 'Production' -and $release.prerelease)) { continue }
            $release
        }
    )

    $releasesToDelete = @($channelReleases | Sort-Object -Property created_at -Descending | Select-Object -Skip 2)
    foreach ($release in $releasesToDelete) {
        Invoke-NativeCommand -FilePath $GitHubCliPath -Arguments @('release', 'delete', $release.tag_name, '--repo', $Repository, '--yes') -FailureHint "Unable to apply release retention in '$Repository'." | Out-Null
    }
}

function Invoke-Publish {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] [ValidateSet('Development', 'Production')] [string]$Channel,
        [Parameter(Mandatory)] [string]$PublishDirectory,
        [Parameter(Mandatory)] [string]$Repository,
        [string]$RequiredBranch,
        [switch]$DryRun,
        [string[]]$KnownTags,
        [string]$ArchiveDirectory
    )

    $gitHubCliPath = Get-GitHubCliPath
    $authentication = Assert-GitHubAuthentication -GitHubCliPath $gitHubCliPath -Repository $Repository -ExpectedGitHubAccount $script:ExpectedGitHubAccount
    $gitState = Test-GitPreconditions -GitHubCliPath $gitHubCliPath -Repository $Repository -RequiredBranch $RequiredBranch
    $tags = if ($PSBoundParameters.ContainsKey('KnownTags')) { @($KnownTags) } else { Get-GitHubTags -GitHubCliPath $gitHubCliPath -Repository $Repository }
    $version = Get-NextReleaseVersion -Channel $Channel -Tags $tags
    $artifact = New-PublishArchive -PublishDirectory $PublishDirectory -Version $version -ArchiveDirectory $ArchiveDirectory

    Write-Host "Prepared $version ($($artifact.Sha256)) from commit $($gitState.Commit) as GitHub account $($authentication.Account)."
    Write-Host "Archive: $($artifact.ArchivePath)"
    Write-Host "Checksum: $($artifact.ChecksumPath)"
    if ($DryRun) {
        Write-Host 'Dry run: no GitHub Release was created and no existing release was deleted.'
        return [pscustomobject]@{ Version = $version; Artifact = $artifact; Git = $gitState; Account = $authentication.Account; DryRun = $true }
    }

    if ($Channel -eq 'Production') { Confirm-ProductionPublication -Version $version -Repository $Repository -Commit $gitState.Commit }
    Publish-GitHubRelease -GitHubCliPath $gitHubCliPath -Repository $Repository -Channel $Channel -Version $version -Commit $gitState.Commit -Artifact $artifact
    # Retention only starts after a successful release creation. Git tags remain after release deletion.
    Remove-ObsoleteGitHubReleases -GitHubCliPath $gitHubCliPath -Repository $Repository -Channel $Channel
    Write-Host "Published $version and retained the two newest $Channel releases."
    return [pscustomobject]@{ Version = $version; Artifact = $artifact; Git = $gitState; Account = $authentication.Account; DryRun = $false }
}

# Dot-sourcing is used by the PowerShell tests without starting a publication.
if ($MyInvocation.InvocationName -eq '.') { return }

try {
    if ([string]::IsNullOrWhiteSpace($Channel) -or [string]::IsNullOrWhiteSpace($PublishDirectory)) {
        throw 'Channel and PublishDirectory are required.'
    }

    Invoke-Publish @PSBoundParameters | Out-Null
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
