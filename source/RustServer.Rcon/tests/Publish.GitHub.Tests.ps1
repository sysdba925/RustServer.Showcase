$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot '..\scripts\publish.ps1'
$scriptText = Get-Content -LiteralPath $scriptPath -Raw
if ($scriptText -match 'git\s+ls-remote|git\s+fetch|git\s+push') {
    throw 'Publisher contains a prohibited network Git command.'
}

. $scriptPath

function Assert-Throws {
    param(
        [Parameter(Mandatory)] [scriptblock]$Action,
        [Parameter(Mandatory)] [string]$ExpectedMessage
    )

    try {
        & $Action
        throw "${ExpectedMessage}: no exception was thrown."
    }
    catch {
        if ($_.Exception.Message -eq "${ExpectedMessage}: no exception was thrown.") { throw }
        if ($_.Exception.Message -notmatch $ExpectedMessage) { throw }
    }
}

$script:calls = [System.Collections.Generic.List[string]]::new()
$script:AuthenticationFails = $false
$script:Account = 'sysdba925'
$script:RepositoryResult = 'sysdba925/RustServer.Rcon'
$script:RemoteSha = '0123456789abcdef0123456789abcdef01234567'

function Get-GitHubCliPath { 'gh' }
function Invoke-NativeCommand {
    param([string]$FilePath, [string[]]$Arguments)

    $call = "$FilePath $($Arguments -join ' ')"
    $script:calls.Add($call)
    switch -Regex ($call) {
        '^gh auth status --hostname github\.com$' {
            if ($script:AuthenticationFails) { throw 'gh authentication failed' }
            return 'Logged in to github.com as sysdba925'
        }
        '^gh api user --jq \.login$' { return $script:Account }
        '^gh api repos/sysdba925/RustServer\.Rcon --jq \.full_name$' { return $script:RepositoryResult }
        '^gh api repos/sysdba925/RustServer\.Rcon/git/ref/heads/develop --jq \.object\.sha$' { return $script:RemoteSha }
        '^git rev-parse --is-inside-work-tree$' { return 'true' }
        '^git status --porcelain$' { return '' }
        '^git branch --show-current$' { return 'develop' }
        '^git remote get-url origin$' { return 'https://github.com/sysdba925/RustServer.Rcon.git' }
        '^git config --get branch\.develop\.remote$' { return 'origin' }
        '^git config --get branch\.develop\.merge$' { return 'refs/heads/develop' }
        '^git rev-parse HEAD$' { return '0123456789abcdef0123456789abcdef01234567' }
        default { throw "Unexpected native command: $call" }
    }
}

$auth = Assert-GitHubAuthentication -GitHubCliPath (Get-GitHubCliPath) -Repository 'sysdba925/RustServer.Rcon' -ExpectedGitHubAccount 'sysdba925'
if ($auth.Account -ne 'sysdba925') { throw 'Authenticated GitHub account was not returned.' }
$git = Test-GitPreconditions -GitHubCliPath 'gh' -Repository 'sysdba925/RustServer.Rcon' -RequiredBranch develop
if ($git.Commit -ne '0123456789abcdef0123456789abcdef01234567') { throw 'Git preconditions did not return the verified HEAD.' }
if (-not ($script:calls | Where-Object { $_ -match 'git/ref/heads/develop' })) { throw 'Remote HEAD was not queried through gh api.' }

$script:RemoteSha = 'fedcba9876543210fedcba9876543210fedcba98'
Assert-Throws -Action {
    Test-GitPreconditions -GitHubCliPath 'gh' -Repository 'sysdba925/RustServer.Rcon' -RequiredBranch develop | Out-Null
} -ExpectedMessage 'not the current commit'

$script:AuthenticationFails = $true
Assert-Throws -Action {
    Assert-GitHubAuthentication -GitHubCliPath 'gh' -Repository 'sysdba925/RustServer.Rcon' -ExpectedGitHubAccount 'sysdba925' | Out-Null
} -ExpectedMessage 'not authenticated'

$script:AuthenticationFails = $false
$script:Account = 'other-account'
Assert-Throws -Action {
    Assert-GitHubAuthentication -GitHubCliPath 'gh' -Repository 'sysdba925/RustServer.Rcon' -ExpectedGitHubAccount 'sysdba925' | Out-Null
} -ExpectedMessage 'publishing requires'

$script:Account = 'sysdba925'
$script:RepositoryResult = 'other-owner/other-repository'
Assert-Throws -Action {
    Assert-GitHubAuthentication -GitHubCliPath 'gh' -Repository 'sysdba925/RustServer.Rcon' -ExpectedGitHubAccount 'sysdba925' | Out-Null
} -ExpectedMessage 'not expected repository'

if ($script:calls | Where-Object { $_ -match 'ls-remote|\bfetch\b|\bpush\b' }) {
    throw 'Publisher attempted a prohibited network Git command.'
}

Write-Host 'Passed mocked gh authentication, account, repository, remote HEAD, and SHA-mismatch checks.'
