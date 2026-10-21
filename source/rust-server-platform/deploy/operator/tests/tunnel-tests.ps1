$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path (Split-Path $PSScriptRoot) 'rust-clean.ps1'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($name in @('Get-PortListeners', 'Stop-Tunnel', 'Start-Tunnel')) {
    $definition = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    Invoke-Expression $definition.Extent.Text
}
$localPort = 18080
$script:owners = @(123, 456)
$script:stopped = @()
function Get-NetTCPConnection {
    param($LocalPort, $State)
    if ($LocalPort -ne 18080 -or $State -ne 'Listen') { throw 'Wrong port or state' }
    foreach ($id in $script:owners) { [pscustomobject]@{ OwningProcess = $id } }
}
function Get-Process { param($Id) [pscustomobject]@{ Id = $Id; ProcessName = "owner$Id" } }
function Stop-Process {
    param($InputObject, [switch]$Force)
    if ($script:failStop) { throw 'Access denied' }
    $script:stopped += $InputObject.Id
    $script:owners = @($script:owners | Where-Object { $_ -ne $InputObject.Id })
}
Stop-Tunnel
if (($script:stopped -join ',') -ne '123,456' -or $script:owners.Count) { throw 'Listeners were not stopped.' }
Stop-Tunnel
$script:owners = @(789); $script:failStop = $true
try { Stop-Tunnel; throw 'Expected an error when stopping fails.' }
catch { if ($_.Exception.Message -notmatch 'Could not stop PID 789.*Access denied') { throw } }
$script:failStop = $false
# The start action must free the port before it attempts to create SSH state.
function New-Item { throw 'start reached' }
try { Start-Tunnel; throw 'Expected start sentinel.' }
catch { if ($_.Exception.Message -ne 'start reached') { throw } }
if ($script:owners.Count -or $script:stopped[-1] -ne 789) { throw 'Start did not free the port.' }
Write-Host 'Tunnel tests passed.'
