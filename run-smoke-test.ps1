param([string]$Executable = (Join-Path $PSScriptRoot 'artifacts\YolanLive-win-x64\YolanLive.exe'))
& (Join-Path $PSScriptRoot 'with-build-cache.ps1') {
    $process = Start-Process -FilePath $Executable -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Smoke check failed: $($process.ExitCode)" }
}