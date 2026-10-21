[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Development', 'Production')] [string]$Channel,
    [string]$Repository = 'sysdba925/RustServer.Web',
    [string]$RequiredBranch = 'develop',
    [string]$ArchiveDirectory,
    [switch]$DryRun,
    [string[]]$KnownTags,
    [string]$GitHubCliPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:PackageName = 'RustServer.Web'
$script:ExpectedGitHubAccount = 'sysdba925'
$script:RuntimeIdentifier = 'linux-x64'
$script:RequiredEntryFile = 'index.html'
$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:DistributionDirectory = Join-Path $script:RepositoryRoot 'dist'

$script:PublishLogPath = Join-Path ([IO.Path]::GetTempPath()) ("RustServer.Web-publish-{0}-{1}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'), [Guid]::NewGuid().ToString('N'))
$script:PublishStage = 'Initialization'

function Protect-DiagnosticText {
    param([AllowEmptyString()] [string]$Text)
    foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
        if ($entry.Key -match '(?i)token|secret|password|credential|api.?key|auth' -and $entry.Value -and ([string]$entry.Value).Length -ge 4) {
            $Text = $Text.Replace([string]$entry.Value, '[REDACTED]')
        }
    }
    $Text = $Text -replace '(?i)(https?://)[^\s/@]+:[^\s/@]+@', '$1[REDACTED]@'
    $Text = $Text -replace '(?i)(bearer\s+)[^\s]+', '$1[REDACTED]'
    $Text = $Text -replace '(?i)((?:token|password|secret|credential|api[_-]?key|_auth)(?:\s*[=:]\s*|\s+))["'']?[^\s"'']+', '$1[REDACTED]'
    $Text = $Text -replace '\b(?:gh[pousr]_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+)\b', '[REDACTED]'
    return $Text
}

function Write-Diagnostic {
    param([AllowEmptyString()] [string]$Message)
    $safe = Protect-DiagnosticText -Text $Message
    [IO.File]::AppendAllText($script:PublishLogPath, $safe + [Environment]::NewLine, [Text.Encoding]::UTF8)
    Write-Host $safe
}

function Write-PublishStep {
    param([Parameter(Mandatory)] [string]$Message)

    $script:PublishStage = $Message
    Write-Diagnostic "==> $Message"
}

function Get-Sha256Hash {
    [OutputType([string])]
    param([Parameter(Mandatory)] [string]$Path)

    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = [IO.File]::OpenRead($Path)
        try {
            return ([BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $hasher.Dispose()
    }
}

function Invoke-NativeCommand {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$FailureHint
    )

    $commandText = "$FilePath $($Arguments -join ' ')".Trim()
    Write-Diagnostic "Stage: $script:PublishStage`nCommand: $commandText"
    if ($null -eq (Get-Command $FilePath -ErrorAction SilentlyContinue)) {
        throw (Protect-DiagnosticText -Text "Required command '$FilePath' was not found. $FailureHint".Trim())
    }

    # Capture ErrorRecord objects as text without PowerShell's formatted error
    # output, which can obscure or truncate native stderr.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $FilePath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    $stdout = ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    $stderr = ($output | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    $outputText = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    Write-Diagnostic "Exit code: $exitCode`n--- stdout ---`n$stdout`n--- stderr ---`n$stderr`n--- end command ---"
    if ($exitCode -ne 0) {
        $suffix = if ([string]::IsNullOrWhiteSpace($FailureHint)) { '' } else { " $FailureHint" }
        throw (Protect-DiagnosticText -Text "Stage: $script:PublishStage`nCommand: $commandText`nExit code: $exitCode.$suffix`n--- stdout ---`n$stdout`n--- stderr ---`n$stderr")
    }
    return $outputText.Trim()
}

function Get-GitHubCliPath {
    [OutputType([string])]
    param([string]$ConfiguredPath)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        if (-not (Test-Path -LiteralPath $ConfiguredPath -PathType Leaf)) {
            throw "GitHub CLI was not found at '$ConfiguredPath'."
        }

        return (Resolve-Path -LiteralPath $ConfiguredPath).Path
    }

    $fromPath = Get-Command 'gh' -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) { return $fromPath.Source }

    $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $standardPath = Join-Path -Path $programFiles -ChildPath 'GitHub CLI\gh.exe'
    if (Test-Path -LiteralPath $standardPath -PathType Leaf) { return $standardPath }

    throw "GitHub CLI ('gh') was not found on PATH or at '$standardPath'. Install it and authenticate with 'gh auth login'."
}

function ConvertFrom-GitHubRemoteUrl {
    [OutputType([string])]
    param([Parameter(Mandatory)] [string]$Url)

    $normalizedUrl = $Url.Trim().TrimEnd('/') -replace '\.git$', ''
    $match = [regex]::Match($normalizedUrl, '^(?:https?://|ssh://git@|git@)github\.com[:/](?<owner>[A-Za-z0-9_.-]+)/(?<repository>[A-Za-z0-9_.-]+)$')
    if (-not $match.Success) { throw "Origin remote '$Url' is not a GitHub repository URL." }

    return "$($match.Groups['owner'].Value)/$($match.Groups['repository'].Value)"
}

function Assert-GitHubAuthentication {
    [OutputType([pscustomobject])]
    param([Parameter(Mandatory)] [string]$Path, [Parameter(Mandatory)] [string]$TargetRepository)

    try {
        Invoke-NativeCommand -FilePath $Path -Arguments @('auth', 'status', '--hostname', 'github.com') -FailureHint "Run 'gh auth login --hostname github.com' and grant the repo scope." | Out-Null
    }
    catch {
        throw "GitHub CLI is not authenticated for github.com. $($_.Exception.Message)"
    }

    $account = Invoke-NativeCommand -FilePath $Path -Arguments @('api', 'user', '--jq', '.login') -FailureHint 'Unable to determine the active GitHub account.'
    if (-not $account.Equals($script:ExpectedGitHubAccount, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "GitHub CLI is authenticated as '$account', but publishing requires '$script:ExpectedGitHubAccount'."
    }

    $resolvedRepository = Invoke-NativeCommand -FilePath $Path -Arguments @('api', "repos/$TargetRepository", '--jq', '.full_name') -FailureHint "The active account '$account' cannot access '$TargetRepository'."
    if (-not $resolvedRepository.Equals($TargetRepository, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "GitHub API resolved '$resolvedRepository', not expected repository '$TargetRepository'."
    }

    return [pscustomobject]@{ Account = $account; Repository = $resolvedRepository }
}

function Assert-GitPreconditions {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$TargetRepository,
        [Parameter(Mandatory)] [string]$Branch
    )

    $insideRepository = Invoke-NativeCommand -FilePath 'git' -Arguments @('rev-parse', '--is-inside-work-tree')
    if ($insideRepository -ne 'true') { throw 'Publish must be run from a Git working tree.' }
    $gitRoot = (Resolve-Path (Invoke-NativeCommand -FilePath 'git' -Arguments @('rev-parse', '--show-toplevel'))).Path
    if (-not $gitRoot.Equals($script:RepositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Publish must be run from '$script:RepositoryRoot'."
    }

    $status = Invoke-NativeCommand -FilePath 'git' -Arguments @('status', '--porcelain')
    if (-not [string]::IsNullOrWhiteSpace($status)) { throw 'Publish requires a clean Git working tree.' }

    $branch = Invoke-NativeCommand -FilePath 'git' -Arguments @('branch', '--show-current')
    if ($branch -ne $Branch) { throw "Publish requires branch '$Branch', but the current branch is '$branch'." }

    $origin = ConvertFrom-GitHubRemoteUrl -Url (Invoke-NativeCommand -FilePath 'git' -Arguments @('remote', 'get-url', 'origin'))
    if (-not $origin.Equals($TargetRepository, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Origin remote points to '$origin', not expected repository '$TargetRepository'."
    }

    $remoteName = Invoke-NativeCommand -FilePath 'git' -Arguments @('config', '--get', "branch.$branch.remote")
    $mergeReference = Invoke-NativeCommand -FilePath 'git' -Arguments @('config', '--get', "branch.$branch.merge")
    if ($remoteName -ne 'origin' -or $mergeReference -ne "refs/heads/$branch") {
        throw "Branch '$branch' must track 'origin/$branch' before publishing."
    }

    $commit = Invoke-NativeCommand -FilePath 'git' -Arguments @('rev-parse', 'HEAD')
    $remoteCommit = Invoke-NativeCommand -FilePath $Path -Arguments @('api', "repos/$TargetRepository/git/ref/heads/$branch", '--jq', '.object.sha') -FailureHint "Unable to resolve GitHub branch '$TargetRepository/$branch'."
    if (-not $commit.Equals($remoteCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "HEAD ($commit) is not the current commit of origin/$branch. Push and synchronize the branch before publishing."
    }

    return [pscustomobject]@{ Branch = $branch; Commit = $commit }
}

function ConvertFrom-ReleaseTag {
    [OutputType([pscustomobject])]
    param([Parameter(Mandatory)] [string]$Tag)

    $match = [regex]::Match($Tag, '^v(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-dev\.(?<sequence>[1-9]\d*))?$')
    if (-not $match.Success) { return $null }

    return [pscustomobject]@{
        Major = [int]$match.Groups['major'].Value
        Minor = [int]$match.Groups['minor'].Value
        Patch = [int]$match.Groups['patch'].Value
        Sequence = if ($match.Groups['sequence'].Success) { [int]$match.Groups['sequence'].Value } else { $null }
        Channel = if ($match.Groups['sequence'].Success) { 'Development' } else { 'Production' }
    }
}

function Compare-Version {
    [OutputType([int])]
    param([Parameter(Mandatory)] [psobject]$Left, [Parameter(Mandatory)] [psobject]$Right)

    foreach ($part in @('Major', 'Minor', 'Patch')) {
        if ($Left.$part -lt $Right.$part) { return -1 }
        if ($Left.$part -gt $Right.$part) { return 1 }
    }

    return 0
}

function Get-NextReleaseVersion {
    [OutputType([string])]
    param([Parameter(Mandatory)] [ValidateSet('Development', 'Production')] [string]$SelectedChannel, [AllowEmptyCollection()] [string[]]$Tags)

    $versions = @(
        $Tags |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { ConvertFrom-ReleaseTag -Tag $_ } |
            Where-Object { $null -ne $_ }
    )
    $productionVersions = @($versions | Where-Object Channel -eq 'Production' | Sort-Object Major, Minor, Patch -Descending)
    $productionBase = if ($productionVersions.Count -eq 0) {
        [pscustomobject]@{ Major = 0; Minor = 1; Patch = 0 }
    }
    else {
        $latest = $productionVersions[0]
        if ($latest.Patch -eq [int]::MaxValue) { throw "Cannot increment patch version for tag v$($latest.Major).$($latest.Minor).$($latest.Patch)." }
        [pscustomobject]@{ Major = $latest.Major; Minor = $latest.Minor; Patch = $latest.Patch + 1 }
    }

    if ($SelectedChannel -eq 'Production') {
        return "v$($productionBase.Major).$($productionBase.Minor).$($productionBase.Patch)"
    }

    $developmentVersions = @($versions | Where-Object Channel -eq 'Development')
    $developmentBase = $productionBase
    foreach ($version in $developmentVersions) {
        if ((Compare-Version -Left $version -Right $developmentBase) -gt 0) {
            $developmentBase = [pscustomobject]@{ Major = $version.Major; Minor = $version.Minor; Patch = $version.Patch }
        }
    }

    $highestSequence = 0
    foreach ($version in $developmentVersions) {
        if ((Compare-Version -Left $version -Right $developmentBase) -eq 0 -and $version.Sequence -gt $highestSequence) {
            $highestSequence = $version.Sequence
        }
    }

    if ($highestSequence -eq [int]::MaxValue) { throw 'Cannot increment the development release sequence.' }
    return "v$($developmentBase.Major).$($developmentBase.Minor).$($developmentBase.Patch)-dev.$($highestSequence + 1)"
}

function Invoke-ProductionBuild {
    param([Parameter(Mandatory)] [ValidateSet('Development', 'Production')] [string]$SelectedChannel)

    Write-PublishStep 'Installing locked dependencies (npm ci)'
    Invoke-NativeCommand -FilePath 'npm.cmd' -Arguments @('ci') -FailureHint 'Unable to install locked Web dependencies.' | Out-Null
    Write-PublishStep 'Running TypeScript typecheck'
    Invoke-NativeCommand -FilePath 'npm.cmd' -Arguments @('run', 'typecheck') -FailureHint 'TypeScript verification failed.' | Out-Null
    Write-PublishStep 'Running tests'
    Invoke-NativeCommand -FilePath 'npm.cmd' -Arguments @('run', 'test') -FailureHint 'Web tests failed.' | Out-Null
    Write-PublishStep "Building optimized Vite distribution ($SelectedChannel Release)"
    Invoke-NativeCommand -FilePath 'npm.cmd' -Arguments @('run', 'build') -FailureHint 'Vite production build failed.' | Out-Null
}

function Test-StaticDistribution {
    [OutputType([pscustomobject])]
    param([Parameter(Mandatory)] [string]$Directory)

    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { throw "Distribution directory '$Directory' does not exist." }
    $resolvedDirectory = (Resolve-Path -LiteralPath $Directory).Path
    $entry = Get-Item -LiteralPath (Join-Path $resolvedDirectory $script:RequiredEntryFile) -Force -ErrorAction SilentlyContinue
    if ($null -eq $entry -or $entry.PSIsContainer -or $entry.Length -eq 0) { throw "Distribution must contain a non-empty '$script:RequiredEntryFile' at its root." }

    $files = @(Get-ChildItem -LiteralPath $resolvedDirectory -File -Recurse -Force)
    if ($files.Count -eq 0) { throw "Distribution directory '$resolvedDirectory' is empty." }

    foreach ($item in @(Get-ChildItem -LiteralPath $resolvedDirectory -Recurse -Force)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Distribution contains a symbolic link or reparse point: '$($item.FullName)'."
        }
    }

    foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($resolvedDirectory.Length).TrimStart([char[]]'\\/').Replace('\', '/')
        if ($relativePath -match '^(?:node_modules|src|\.git)(?:/|$)' -or $relativePath -match '(^|/)(?:\.env(?:\..+)?|secrets?|credentials?)(?:[._-].*)?$') {
            throw "Distribution contains a prohibited development or secret file '$relativePath'."
        }
    }

    return [pscustomobject]@{ Directory = $resolvedDirectory; Files = $files }
}

function New-ReleaseArchive {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] [psobject]$Distribution,
        [Parameter(Mandatory)] [string]$Version,
        [string]$OutputDirectory
    )

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $script:RepositoryRoot 'artifacts/releases' }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $resolvedOutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
    $archiveName = "$($script:PackageName)-$Version-$($script:RuntimeIdentifier).tar.gz"
    $archivePath = Join-Path $resolvedOutputDirectory $archiveName
    $checksumPath = "$archivePath.sha256"

    $temporaryDirectory = Join-Path $resolvedOutputDirectory ".${archiveName}.$([Guid]::NewGuid().ToString('N')).tmp"
    $temporaryArchivePath = Join-Path $temporaryDirectory $archiveName
    $temporaryChecksumPath = "$temporaryArchivePath.sha256"
    New-Item -ItemType Directory -Path $temporaryDirectory -ErrorAction Stop | Out-Null
    try {
        Invoke-NativeCommand -FilePath 'tar.exe' -Arguments @('-czf', $temporaryArchivePath, '-C', $Distribution.Directory, '.') -FailureHint 'Unable to create the release archive.' | Out-Null
        $archiveListing = Invoke-NativeCommand -FilePath 'tar.exe' -Arguments @('-tzf', $temporaryArchivePath) -FailureHint 'Unable to inspect the release archive.'
        $entries = @($archiveListing -split "`r?`n" | ForEach-Object { $_.Trim() -replace '^(?:\./)+', '' })
        if ($entries -notcontains $script:RequiredEntryFile) { throw "Release archive does not contain '$script:RequiredEntryFile' at its root." }
        foreach ($file in $Distribution.Files) {
            $relativePath = $file.FullName.Substring($Distribution.Directory.Length).TrimStart([char[]]'\\/').Replace('\', '/')
            if ($entries -notcontains $relativePath) { throw "Release archive does not contain '$relativePath'." }
        }

        $hash = Get-Sha256Hash -Path $temporaryArchivePath
        [IO.File]::WriteAllText($temporaryChecksumPath, "$hash  $archiveName$([Environment]::NewLine)", [Text.Encoding]::ASCII)
        Move-ReleaseArtifactFile -SourcePath $temporaryArchivePath -DestinationPath $archivePath
        Move-ReleaseArtifactFile -SourcePath $temporaryChecksumPath -DestinationPath $checksumPath
    }
    finally {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }

    return [pscustomobject]@{ ArchiveName = $archiveName; ArchivePath = $archivePath; ChecksumPath = $checksumPath; Sha256 = $hash }
}

function Move-ReleaseArtifactFile {
    param(
        [Parameter(Mandatory)] [string]$SourcePath,
        [Parameter(Mandatory)] [string]$DestinationPath
    )

    if (Test-Path -LiteralPath $DestinationPath) {
        $existing = Get-Item -LiteralPath $DestinationPath -Force
        if ($existing.PSIsContainer -or (($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw "Refusing to replace an unsafe release artifact path '$DestinationPath'."
        }

        # Only the exact, calculated archive or checksum path is replaceable.
        # The staged source lives beside it, so a retry cannot touch unrelated files.
        Remove-Item -LiteralPath $DestinationPath -Force -ErrorAction Stop
    }

    Move-Item -LiteralPath $SourcePath -Destination $DestinationPath -ErrorAction Stop
}

function Test-ReleaseChecksum {
    param([Parameter(Mandatory)] [psobject]$Artifact)

    $checksum = Get-Content -LiteralPath $Artifact.ChecksumPath -Raw -ErrorAction Stop
    $expectedLine = "$($Artifact.Sha256)  $($Artifact.ArchiveName)$([Environment]::NewLine)"
    if (-not $checksum.Equals($expectedLine, [System.StringComparison]::Ordinal)) {
        throw "Release checksum sidecar is invalid: '$($Artifact.ChecksumPath)'."
    }

    $actualHash = Get-Sha256Hash -Path $Artifact.ArchivePath
    if (-not $actualHash.Equals($Artifact.Sha256, [System.StringComparison]::Ordinal)) {
        throw "Release archive checksum verification failed: '$($Artifact.ArchivePath)'."
    }
}

function Confirm-ProductionPublication {
    param([Parameter(Mandatory)] [string]$Version, [Parameter(Mandatory)] [string]$TargetRepository, [Parameter(Mandatory)] [string]$Commit)

    if (-not [Environment]::UserInteractive) { throw 'Production publishing requires interactive user confirmation.' }
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
    $answer = [System.Windows.Forms.MessageBox]::Show(
        "Create production release $Version in $TargetRepository for commit $Commit?",
        'Confirm GitHub production release',
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Warning,
        [System.Windows.Forms.MessageBoxDefaultButton]::Button2)
    if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) { throw 'Production release was cancelled by the user.' }
}

function Remove-ObsoleteGitHubReleases {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$TargetRepository,
        [Parameter(Mandatory)] [ValidateSet('Development', 'Production')] [string]$SelectedChannel
    )

    $releaseJson = Invoke-NativeCommand -FilePath $Path -Arguments @('api', '--paginate', '--slurp', "repos/$TargetRepository/releases?per_page=100") -FailureHint 'Unable to query GitHub releases for retention.'
    $pages = ConvertFrom-Json -InputObject $releaseJson
    $matchingReleases = @(
        foreach ($page in @($pages)) {
            foreach ($release in @($page)) {
                if ($release.draft) { continue }
                $version = ConvertFrom-ReleaseTag -Tag $release.tag_name
                if ($null -eq $version -or $version.Channel -ne $SelectedChannel) { continue }
                if (($SelectedChannel -eq 'Development' -and -not $release.prerelease) -or ($SelectedChannel -eq 'Production' -and $release.prerelease)) { continue }
                $release
            }
        }
    )

    foreach ($release in @($matchingReleases | Sort-Object created_at -Descending | Select-Object -Skip 2)) {
        # GitHub keeps tags after release deletion, preventing version reuse.
        Invoke-NativeCommand -FilePath $Path -Arguments @('release', 'delete', $release.tag_name, '--repo', $TargetRepository, '--yes') -FailureHint "Unable to apply release retention in '$TargetRepository'." | Out-Null
    }
}

function Invoke-Publish {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] [ValidateSet('Development', 'Production')] [string]$SelectedChannel,
        [Parameter(Mandatory)] [string]$TargetRepository,
        [Parameter(Mandatory)] [string]$Branch,
        [string]$OutputDirectory,
        [switch]$IsDryRun,
        [string[]]$ReleaseTags,
        [string]$ConfiguredGitHubCliPath
    )

    Push-Location $script:RepositoryRoot
    try {
        Write-PublishStep 'Locating GitHub CLI'
        $path = Get-GitHubCliPath -ConfiguredPath $ConfiguredGitHubCliPath
        Write-PublishStep 'Validating GitHub authentication and repository access'
        $authentication = Assert-GitHubAuthentication -Path $path -TargetRepository $TargetRepository
        Write-PublishStep 'Validating Git repository, branch, and remote commit'
        $git = Assert-GitPreconditions -Path $path -TargetRepository $TargetRepository -Branch $Branch
        Write-PublishStep 'Calculating the next release version'
        if ($null -ne $ReleaseTags -and $ReleaseTags.Count -gt 0) {
            $tags = @($ReleaseTags)
        }
        else {
            $tagOutput = Invoke-NativeCommand -FilePath $path -Arguments @('api', '--paginate', "repos/$TargetRepository/tags?per_page=100", '--jq', '.[].name') -FailureHint 'Unable to read GitHub tags.'
            $tags = @($tagOutput -split "`r?`n")
        }
        $version = Get-NextReleaseVersion -SelectedChannel $SelectedChannel -Tags $tags

        Invoke-ProductionBuild -SelectedChannel $SelectedChannel
        Write-PublishStep 'Validating the built static distribution'
        $distribution = Test-StaticDistribution -Directory $script:DistributionDirectory
        Write-PublishStep "Packaging local artifacts for $version"
        $artifact = New-ReleaseArchive -Distribution $distribution -Version $version -OutputDirectory $OutputDirectory
        Write-PublishStep 'Verifying the release checksum'
        Test-ReleaseChecksum -Artifact $artifact
        Write-Host "Prepared $version ($($artifact.Sha256)) from commit $($git.Commit) as GitHub account $($authentication.Account)."
        Write-Host "Archive: $($artifact.ArchivePath)"
        Write-Host "Checksum: $($artifact.ChecksumPath)"

        if ($IsDryRun) { return [pscustomobject]@{ Version = $version; Artifact = $artifact; DryRun = $true; ReleaseUrl = $null } }
        if ($SelectedChannel -eq 'Production') { Confirm-ProductionPublication -Version $version -TargetRepository $TargetRepository -Commit $git.Commit }

        Write-PublishStep "Creating the GitHub Release for $version"
        $arguments = @('release', 'create', $version, $artifact.ArchivePath, $artifact.ChecksumPath, '--repo', $TargetRepository, '--target', $git.Commit, '--title', $version, '--notes', "Automated $SelectedChannel release from commit $($git.Commit).")
        if ($SelectedChannel -eq 'Development') { $arguments += '--prerelease' }
        Invoke-NativeCommand -FilePath $path -Arguments $arguments -FailureHint "Unable to create the GitHub release in '$TargetRepository'." | Out-Null
        Write-PublishStep "Applying $SelectedChannel release retention"
        Remove-ObsoleteGitHubReleases -Path $path -TargetRepository $TargetRepository -SelectedChannel $SelectedChannel
        Write-Host "Published $version and retained the two newest $SelectedChannel releases."
        return [pscustomobject]@{ Version = $version; Artifact = $artifact; DryRun = $false; ReleaseUrl = "https://github.com/$TargetRepository/releases/tag/$version" }
    }
    finally { Pop-Location }
}

try {
    Write-Diagnostic "PowerShell version: $($PSVersionTable.PSVersion)"
    foreach ($versionCommand in @(@('node.exe', '--version'), @('npm.cmd', '--version'))) {
        try { Invoke-NativeCommand -FilePath $versionCommand[0] -Arguments @($versionCommand[1]) | Out-Null }
        catch { Write-Diagnostic "Version probe failed: $($_.Exception.Message)" }
    }
    $result = Invoke-Publish -SelectedChannel $Channel -TargetRepository $Repository -Branch $RequiredBranch -OutputDirectory $ArchiveDirectory -IsDryRun:$DryRun -ReleaseTags $KnownTags -ConfiguredGitHubCliPath $GitHubCliPath
    if ($result.DryRun) {
        Write-Host "DRY RUN COMPLETE: final version $($result.Version); no GitHub Release was created."
    }
    else {
        Write-Host "PUBLISH SUCCESS: final version $($result.Version)"
        Write-Host "Release URL: $($result.ReleaseUrl)"
    }
}
catch {
    Write-Diagnostic ("PUBLISH FAILED at stage '$script:PublishStage': " + (Protect-DiagnosticText -Text ($_ | Out-String)))
    Write-Error (Protect-DiagnosticText -Text "PUBLISH FAILED: $($_.Exception.Message)") -ErrorAction Continue
    exit 1
}
finally {
    Write-Host "Publish log: $script:PublishLogPath"
}
