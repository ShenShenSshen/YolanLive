param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\artifacts\YolanLive-win-x64\YolanLive.exe'),
    [switch]$Live,
    [switch]$NoProxy
)
$ErrorActionPreference = 'Stop'
$r = Split-Path $PSScriptRoot -Parent
& (Join-Path $r 'with-build-cache.ps1') {
    $previous = $env:YOLANLIVE_SMOKE_PROFILE
    try {
        $profile = Join-Path $env:TEMP ('YolanLive-smoke-test\update-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $profile -Force | Out-Null
        $env:YOLANLIVE_SMOKE_PROFILE = $profile
        $arguments = @('--smoke-test', $(if ($Live) {'--update-download-check'} else {'--update-offline-check'}))
        if ($NoProxy) { $arguments += '--update-no-proxy' }
        $process = Start-Process -FilePath $Exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if (!$process.WaitForExit(180000)) { Stop-Process -Id $process.Id; throw 'Update checks timed out' }
        $log = Join-Path $profile $(if ($Live) {'update-check.log'} else {'update-offline.log'})
        Get-Content -Encoding UTF8 -LiteralPath $log
        'Profile=' + $profile
        if ($process.ExitCode -ne 0) { throw ('Update checks failed: exit=' + $process.ExitCode) }
    }
    finally { $env:YOLANLIVE_SMOKE_PROFILE = $previous }
}
