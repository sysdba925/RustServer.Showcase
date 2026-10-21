function Assert-ShellLineEndings {
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [System.IO.FileInfo[]]$Files)

    foreach ($file in $Files) {
        if ($file.Extension -ine '.sh') { continue }
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        for ($i = 1; $i -lt $bytes.Length; $i++) {
            if ($bytes[$i - 1] -eq 13 -and $bytes[$i] -eq 10) {
                throw "Shell script contains CRLF line endings: '$($file.FullName)'. Use LF and publish again."
            }
        }
    }
}
