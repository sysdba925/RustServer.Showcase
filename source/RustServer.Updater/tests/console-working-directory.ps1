# Exercise a published API as a separate process, with systemd-like working-directory semantics.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("updater-console-cwd-" + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $scratch 'api'
$working = Join-Path $scratch 'working'
New-Item -ItemType Directory -Path $working -Force | Out-Null
@'
[Defaults]
RepositoryOwner=sysdba925
InstallRoot=/opt/rustserver
HealthScheme=http
HealthHost=127.0.0.1
HealthPath=/health

[Application:RustServer.Updater]
Repository=RustServer.Updater
Directory=updater
Service=rustserver-updater
Entrypoint=RustServer.Updater.Api.dll
HealthPort=8080
'@ | Set-Content -LiteralPath (Join-Path $scratch 'applications.ini')
$process = $null
try {
    dotnet publish (Join-Path $root 'src/RustServer.Updater.Api/RustServer.Updater.Api.csproj') --configuration Release --output $publish
    if ($LASTEXITCODE -ne 0) { throw 'API publish failed' }

    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'dotnet'
    $start.ArgumentList.Add((Join-Path $publish 'RustServer.Updater.Api.dll'))
    $start.WorkingDirectory = $working
    $start.UseShellExecute = $false
    $start.Environment['ASPNETCORE_URLS'] = "http://127.0.0.1:$port"
    $start.Environment['Operations__StoragePath'] = (Join-Path $scratch 'operations')
    $start.Environment['ApplicationRegistry__StoragePath'] = (Join-Path $scratch 'applications.ini')
    $start.Environment['PackagePreparation__StoragePath'] = (Join-Path $scratch 'staging')
    $start.Environment['DeploymentExecutor__RequestStoragePath'] = (Join-Path $scratch 'requests')
    $start.Environment['DeploymentExecutor__TransactionStoragePath'] = (Join-Path $scratch 'transactions')
    $start.Environment['Logging__LogLevel__Default'] = 'Warning'
    $process = [Diagnostics.Process]::Start($start)
    $base = "http://127.0.0.1:$port"
    $ready = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($process.HasExited) { throw "API exited with code $($process.ExitCode)" }
        try { $response = Invoke-WebRequest "$base/emergency" -TimeoutSec 2; $ready = $true; break }
        catch { Start-Sleep -Milliseconds 100 }
    }
    if (-not $ready) { throw 'API did not become ready' }
    if ($response.StatusCode -ne 200 -or $response.Content -notmatch 'Updater Console') { throw 'Invalid console HTML' }
    foreach ($asset in @('console.css', 'console.js', 'rust.ico')) {
        $response = Invoke-WebRequest "$base/console/assets/$asset" -TimeoutSec 5
        if ($response.StatusCode -ne 200 -or $response.RawContentLength -le 0) { throw "Invalid asset: $asset" }
    }
    Write-Host 'Published console HTML, CSS, JS and favicon served from a different working directory.'
}
finally {
    if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    if ($process) { $process.Dispose() }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
