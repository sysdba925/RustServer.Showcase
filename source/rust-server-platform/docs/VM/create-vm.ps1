# Creates the disposable RustServer Multipass test VM with a stable host-only IP.
# Run from an elevated Windows PowerShell terminal.
[CmdletBinding()]
param(
    [string]$Name = 'rust-clean',
    [string]$GuestIp = '10.77.88.10'
)

$ErrorActionPreference = 'Stop'
$switchName = 'RustServerLab'
$hostIp = '10.77.88.1'

try {
    $admin = [Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $admin.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run PowerShell as Administrator.'
    }
    if (-not (Get-Command multipass -ErrorAction SilentlyContinue)) {
        throw 'Multipass is not installed or is not in PATH.'
    }

    $switch = Get-VMSwitch -Name $switchName -ErrorAction SilentlyContinue
    if ($null -eq $switch) {
        Write-Host "Creating internal switch $switchName..."
        $switch = New-VMSwitch -Name $switchName -SwitchType Internal
    }
    if ($switch.SwitchType -ne 'Internal') {
        throw "Switch $switchName exists, but is not Internal. No changes made."
    }

    $adapterAlias = "vEthernet ($switchName)"
    $addresses = @(Get-NetIPAddress -InterfaceAlias $adapterAlias -AddressFamily IPv4 -ErrorAction SilentlyContinue)
    $matchingHostAddress = @($addresses | Where-Object { $_.IPAddress -eq $hostIp -and $_.PrefixLength -eq 24 })
    if ($matchingHostAddress.Count -eq 0) {
        if ($addresses.Count -gt 0) {
            throw "Unexpected IPv4 address on $adapterAlias. Expected $hostIp/24. No changes made."
        }
        Write-Host "Configuring host IP $hostIp/24..."
        New-NetIPAddress -InterfaceAlias $adapterAlias -IPAddress $hostIp -PrefixLength 24 | Out-Null
    }

    & multipass info $Name *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "VM '$Name' already exists. This script never overwrites or deletes VMs."
    }

    $cloudInit = @'
#cloud-config
write_files:
  - path: /etc/netplan/60-rustserver-lab.yaml
    permissions: '0600'
    content: |
      network:
        version: 2
        ethernets:
          eth1:
            dhcp4: false
            addresses: [__GUEST_IP__/24]
runcmd:
  - netplan apply
'@
    $cloudInit = $cloudInit.Replace('__GUEST_IP__', $GuestIp)

    $tempPath = Join-Path ([IO.Path]::GetTempPath()) ("rustserver-cloud-init-{0}.yaml" -f [guid]::NewGuid())
    try {
        [IO.File]::WriteAllText($tempPath, $cloudInit, [Text.Encoding]::ASCII)
        Write-Host "Launching Ubuntu VM '$Name'..."
        & multipass launch 24.04 --name $Name --cpus 2 --memory 2G --disk 10G --network "name=$switchName,mode=manual" --cloud-init $tempPath
        if ($LASTEXITCODE -ne 0) { throw "multipass launch failed (exit code $LASTEXITCODE)." }
    }
    finally {
        if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force }
    }

    Write-Host 'Guest addresses:'
    & multipass exec $Name -- ip -br addr
    if ($LASTEXITCODE -ne 0) { throw 'Could not check guest network interfaces.' }
    Write-Host "Expected stable address: $GuestIp (RustServerLab)"
    Write-Host 'The VM is ready for the RustServer Installer.'
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
