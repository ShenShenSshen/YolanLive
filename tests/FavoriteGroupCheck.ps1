param([string]$Exe = (Join-Path $PSScriptRoot '..\artifacts\YolanLive-favorites-preview\YolanLive.exe'))
$ErrorActionPreference = 'Stop'
$r = Split-Path $PSScriptRoot -Parent
& (Join-Path $r 'with-build-cache.ps1') {
    $previous = $env:YOLANLIVE_SMOKE_PROFILE
    try {
        $profile = Join-Path $env:TEMP ('YolanLive-smoke-test\favorite-groups-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $profile -Force | Out-Null
        $env:YOLANLIVE_SMOKE_PROFILE = $profile
        $process = Start-Process -FilePath $Exe -ArgumentList '--smoke-test','--favorite-groups-check' -WindowStyle Hidden -PassThru
        if (!$process.WaitForExit(60000)) { Stop-Process -Id $process.Id; throw 'Favorite group checks timed out' }
        $out = Join-Path $r 'docs\validation\favorite-groups'
        New-Item -ItemType Directory -Path $out -Force | Out-Null
        $log = Join-Path $profile 'favorite-groups.log'
        Copy-Item -LiteralPath $log -Destination (Join-Path $out 'checks.txt') -Force
        Get-Content -Encoding UTF8 -LiteralPath $log
        'Profile=' + $profile
        if ($process.ExitCode -ne 0) { throw ('Favorite group checks failed: exit=' + $process.ExitCode) }
    }
    finally { $env:YOLANLIVE_SMOKE_PROFILE = $previous }
}
