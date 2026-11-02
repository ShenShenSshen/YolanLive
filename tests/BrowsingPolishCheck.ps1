param(
    [ValidateSet('all','visual','smoke','images')][string]$Mode = 'all',
    [string]$Exe = (Join-Path $PSScriptRoot '..\artifacts\YolanLive-browsing-preview\YolanLive.exe')
)
$ErrorActionPreference = 'Stop'
$r = Split-Path $PSScriptRoot -Parent
& (Join-Path $r 'with-build-cache.ps1') {
    $previous = $env:YOLANLIVE_SMOKE_PROFILE
    try {
        $profile = Join-Path $env:TEMP ('YolanLive-smoke-test\browsing-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $profile -Force | Out-Null
        $env:YOLANLIVE_SMOKE_PROFILE = $profile
        $arguments = @('--smoke-test')
        if ($Mode -eq 'all') { $arguments += '--browsing-polish-checks' }
        if ($Mode -eq 'visual') { $arguments += '--visual-checks' }
        if ($Mode -eq 'images') { $arguments += '--image-checks' }
        $process = Start-Process -FilePath $Exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if (!$process.WaitForExit(180000)) { Stop-Process -Id $process.Id; throw 'Browsing checks timed out' }
        $out = Join-Path $r 'docs\validation\browsing-polish'
        New-Item -ItemType Directory -Path $out -Force | Out-Null
        $name = switch ($Mode) { 'all' {'browsing-polish.log'} 'visual' {'visual-checks.log'} 'smoke' {'smoke-test.log'} 'images' {'category-image-checks.log'} }
        $log = Join-Path $profile $name
        Copy-Item -LiteralPath $log -Destination (Join-Path $out ($Mode + '.txt')) -Force
        Get-Content -Encoding UTF8 -LiteralPath $log
        'Profile=' + $profile
        if ($process.ExitCode -ne 0) { throw ('Browsing checks failed: exit=' + $process.ExitCode) }
    }
    finally { $env:YOLANLIVE_SMOKE_PROFILE = $previous }
}
