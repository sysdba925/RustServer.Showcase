[CmdletBinding()]
param([ValidateSet('Bootstrap','Clean','Tunnel','CloseTunnel')][string]$Action)
$ErrorActionPreference = 'Stop'
$failureExitCode = 1
$deploy = Split-Path -Parent $PSScriptRoot
$helper = Join-Path $PSScriptRoot 'vm-rustserver.sh'
$stateDir = Join-Path $env:LOCALAPPDATA 'RustServer'
$errorLog = Join-Path $stateDir 'operator-tunnel.stderr.log'

function Read-EnvFile([string]$Path, [string[]]$Names) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required file missing: $Path" }
    $result = @{}
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if ($line -match '^\s*(#|$)') { continue }
        if ($line -notmatch '^([A-Z][A-Z0-9_]*)=(.*)$') { throw "Invalid line in $Path" }
        $key, $value = $Matches[1], $Matches[2]
        if ($key -notin $Names -or $result.ContainsKey($key) -or -not $value) { throw "Invalid or duplicate setting $key in $Path" }
        $result[$key] = $value
    }
    foreach ($name in $Names) { if (-not $result.ContainsKey($name)) { throw "Missing $name in $Path" } }
    return $result
}
function Require-Port($value, $name) {
    $number = 0
    if (-not [int]::TryParse($value, [ref]$number) -or $number -lt 1 -or $number -gt 65535) { throw "Invalid $name" }
    return $number
}
function Initialize-Connection {
    $script:config = Read-EnvFile (Join-Path $PSScriptRoot 'connection.env') @('SSH_HOST','SSH_USER','SSH_PORT','SSH_KEY','UPDATER_PORT','TUNNEL_LOCAL_PORT')
    if ($config.SSH_HOST -notmatch '^[a-zA-Z0-9][a-zA-Z0-9.:-]*$' -or $config.SSH_USER -notmatch '^[a-z_][a-z0-9_-]*$') { throw 'Invalid SSH_HOST or SSH_USER.' }
    $script:sshPort = Require-Port $config.SSH_PORT 'SSH_PORT'
    $script:remotePort = Require-Port $config.UPDATER_PORT 'UPDATER_PORT'
    $script:localPort = Require-Port $config.TUNNEL_LOCAL_PORT 'TUNNEL_LOCAL_PORT'
    $script:key = $config.SSH_KEY
    if ($key -match '^~[/\\]') { $script:key = Join-Path $env:USERPROFILE $key.Substring(2) }
    elseif (-not [IO.Path]::IsPathRooted($key)) { $script:key = Join-Path $PSScriptRoot $key }
    if (-not (Test-Path -LiteralPath $key -PathType Leaf)) { throw "SSH key not found: $key" }
    if ($key.Contains('"')) { throw 'Invalid SSH key path.' }
    $script:target = "$($config.SSH_USER)@$($config.SSH_HOST)"
    $script:ssh = (Get-Command ssh -CommandType Application | Select-Object -First 1).Source
    $script:scp = (Get-Command scp -CommandType Application | Select-Object -First 1).Source
    $script:common = @('-o','BatchMode=yes','-o','IdentitiesOnly=yes','-i',$key)
}
function Invoke-Native($exe, [string[]]$arguments) {
    $old = $ErrorActionPreference
    try { $ErrorActionPreference = 'Continue'; & $exe @arguments; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $old }
    if ($code -ne 0) { $script:failureExitCode = $code; throw "Command failed (exit $code)." }
}
function Invoke-RemoteOperation([string]$operation) {
    $file = Join-Path $deploy $(if ($operation -eq 'bootstrap') { 'install.sh' } else { 'cleanup.sh' })
    foreach ($path in @($file, $helper)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required file missing: $path" } }
    $payload = $null
    $channel = ''
    if ($operation -eq 'bootstrap') {
        $channel = Read-Host 'Updater release channel (Development or Production)'
        if ($channel -cnotin @('Development', 'Production')) { throw 'Invalid release channel.' }
        $envValues = Read-EnvFile (Join-Path $PSScriptRoot 'secure.env') @('GITHUB_BOOTSTRAP_TOKEN','GITHUB_UPDATER_TOKEN','GITHUB_PLUGINS_TOKEN','GOOGLE_CLIENT_ID','GOOGLE_CLIENT_SECRET','ADMIN_USERNAME','ADMIN_PASSWORD')
        $values = @{'GitHub bootstrap token'=$envValues.GITHUB_BOOTSTRAP_TOKEN; 'GitHub updater all services token'=$envValues.GITHUB_UPDATER_TOKEN; 'GitHub plugin & config token'=$envValues.GITHUB_PLUGINS_TOKEN; GOOGLE_CLIENT_ID=$envValues.GOOGLE_CLIENT_ID; GOOGLE_CLIENT_SECRET=$envValues.GOOGLE_CLIENT_SECRET; ADMIN_USERNAME=$envValues.ADMIN_USERNAME; ADMIN_PASSWORD=$envValues.ADMIN_PASSWORD}
        $payload = [Text.Encoding]::UTF8.GetBytes(($values | ConvertTo-Json -Compress))
        if ($payload.Length -gt 131072) { throw 'Credentials exceed 131072 bytes.' }
    }
    $remoteDir = (& $ssh @common -p $sshPort $target 'umask 077; mktemp -d /tmp/rust-operator.XXXXXXXX')
    if ($LASTEXITCODE -ne 0 -or $remoteDir -notmatch '^/tmp/rust-operator\.[A-Za-z0-9]{8}$') { throw 'Could not create remote working directory.' }
    try {
        Invoke-Native $scp ($common + @('-P', "$sshPort", $file, $helper, "${target}:${remoteDir}/"))
        $process = New-Object Diagnostics.Process
        $process.StartInfo.FileName = $ssh
        $process.StartInfo.Arguments = "-o BatchMode=yes -o IdentitiesOnly=yes -i `"$key`" -p $sshPort $target bash $remoteDir/vm-rustserver.sh $operation $channel"
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.RedirectStandardInput = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.StandardOutputEncoding = New-Object Text.UTF8Encoding($false)
        try {
            if (-not $process.Start()) { throw 'Could not start SSH.' }
            if ($payload) { $process.StandardInput.BaseStream.Write($payload, 0, $payload.Length) }
            $process.StandardInput.Close()
            $marker = if ($operation -eq 'bootstrap') { '__RUST_BOOTSTRAP_RESULT__' } else { '__RUST_CLEAN_RESULT__' }
            $result = $null
            while ($null -ne ($line = $process.StandardOutput.ReadLine())) {
                if ($line -match ('^' + $marker + '=(\d{1,3})$')) { $result = [int]$Matches[1]; break }
                [Console]::Out.WriteLine($line)
            }
            if ($null -ne $result -and -not $process.HasExited) { try { $process.Kill() } catch [InvalidOperationException] {} }
            $process.WaitForExit()
            if ($null -eq $result) { throw "Remote operation did not report a result (SSH exit $($process.ExitCode))." }
            if ($result -ne 0) { $script:failureExitCode = $result; throw "Remote operation failed (exit $result)." }
        } finally { $process.Dispose() }
    } finally {
        try { Invoke-Native $ssh ($common + @('-p', "$sshPort", $target, "rm -f -- $remoteDir/install.sh $remoteDir/cleanup.sh $remoteDir/vm-rustserver.sh; rmdir -- $remoteDir")) }
        catch { Write-Warning "Could not remove remote working directory $remoteDir. Inspect it manually." }
    }
}
function Get-PortListeners {
    try { Get-NetTCPConnection -LocalPort $localPort -State Listen -ErrorAction Stop }
    catch {
        if ($_.FullyQualifiedErrorId -like 'CmdletizationQuery_NotFound*') { return }
        throw
    }
}
function Stop-Tunnel {
    $listeners = @(Get-PortListeners)
    if (-not $listeners) { Write-Host "No process is listening on port $localPort."; return }
    foreach ($ownerId in @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)) {
        # Recheck ownership immediately before stopping a process from the snapshot.
        $current = @(Get-PortListeners | Where-Object OwningProcess -eq $ownerId)
        if (-not $current) { continue }
        try {
            $owner = Get-Process -Id $ownerId -ErrorAction Stop
            Write-Host "Stopping process $($owner.ProcessName) (PID $ownerId) listening on port $localPort."
            Stop-Process -InputObject $owner -Force -ErrorAction Stop
        } catch { throw "Could not stop PID $ownerId on port ${localPort}: $($_.Exception.Message)" }
    }
    for ($i = 0; $i -lt 40; $i++) {
        if (-not @(Get-PortListeners)) {
            Write-Host "Port $localPort is free."
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Port $localPort is still in use after stopping its owner."
}
function Start-Tunnel {
    Stop-Tunnel
    New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
    $args = "-N -T -o BatchMode=yes -o IdentitiesOnly=yes -o ExitOnForwardFailure=yes -o ServerAliveInterval=15 -o ServerAliveCountMax=3 -i `"$key`" -p $sshPort -L 127.0.0.1:${localPort}:127.0.0.1:${remotePort} $target"
    $process = Start-Process -FilePath $ssh -ArgumentList $args -WindowStyle Hidden -PassThru -RedirectStandardError $errorLog
    try {
        $healthy = $false
        for ($i = 0; $i -lt 20; $i++) {
            if ($process.HasExited) { break }
            try { $reply = Invoke-WebRequest "http://127.0.0.1:$localPort/health" -UseBasicParsing -TimeoutSec 2; if ($reply.StatusCode -eq 200 -and $reply.Content -match 'Healthy') { $healthy = $true; break } } catch {}
            Start-Sleep -Milliseconds 250
        }
        if (-not $healthy) { throw "Tunnel could not reach Updater /health. See $errorLog." }
        $url = "http://127.0.0.1:$localPort/emergency"
        Write-Host "Emergency UI: $url"
        try { Start-Process $url } catch { Write-Warning 'Open the URL manually.' }
    } catch { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }; throw }
}
try {
    if (-not $Action) {
        Write-Host "1 - Bootstrap`n2 - Clean (ALL Docker data will be deleted)`n3 - Open Emergency UI`n4 - Close SSH tunnel`n0 - Exit"
        $Action = switch (Read-Host 'Select') { '1' {'Bootstrap'} '2' {'Clean'} '3' {'Tunnel'} '4' {'CloseTunnel'} '0' {''} default {throw 'Unknown menu item.'} }
    }
    if (-not $Action) { exit 0 }
    if ($Action -eq 'CloseTunnel') {
        $connection = Read-EnvFile (Join-Path $PSScriptRoot 'connection.env') @('SSH_HOST','SSH_USER','SSH_PORT','SSH_KEY','UPDATER_PORT','TUNNEL_LOCAL_PORT')
        $localPort = Require-Port $connection.TUNNEL_LOCAL_PORT 'TUNNEL_LOCAL_PORT'
        Stop-Tunnel; exit 0
    }
    Initialize-Connection
    switch ($Action) {
        'Bootstrap' { Invoke-RemoteOperation 'bootstrap' }
        'Clean' {
            Write-Host "WARNING: Clean removes RustServer data and ALL Docker containers, images, volumes and build cache on $($config.SSH_HOST)." -ForegroundColor Yellow
            Invoke-RemoteOperation 'clean'
        }
        'Tunnel' { Start-Tunnel }
    }
    Write-Host "Completed: $Action"
} catch { [Console]::Error.WriteLine("Operation failed: $($_.Exception.Message)"); exit $failureExitCode }
