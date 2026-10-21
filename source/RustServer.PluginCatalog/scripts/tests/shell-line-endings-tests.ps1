$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../assert-shell-line-endings.ps1')

$directory = Join-Path ([IO.Path]::GetTempPath()) ("catalog-line-endings-" + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $directory | Out-Null
    $path = Join-Path $directory 'provision.sh'
    [IO.File]::WriteAllText($path, "#!/bin/bash`necho ok`n")
    Assert-ShellLineEndings -Files @((Get-Item -LiteralPath $path))

    [IO.File]::WriteAllText($path, "#!/bin/bash`r`necho bad`r`n")
    $rejected = $false
    try {
        Assert-ShellLineEndings -Files @((Get-Item -LiteralPath $path))
    }
    catch {
        if (-not $_.Exception.Message.Contains($path) -or -not $_.Exception.Message.Contains('CRLF')) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'CRLF shell script was not rejected.' }
    Write-Host 'Shell line ending tests passed.'
}
finally {
    Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction SilentlyContinue
}
