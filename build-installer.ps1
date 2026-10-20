param([string]$CompilerPath)
& (Join-Path $PSScriptRoot 'with-build-cache.ps1') {
$ErrorActionPreference = 'Stop'

if (-not $CompilerPath) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) { $CompilerPath = $compiler.Source }
    else { $CompilerPath = Join-Path $PSScriptRoot 'artifacts\tools\InnoSetup\ISCC.exe' }
}
if (-not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'Install Inno Setup or pass -CompilerPath with the path to ISCC.exe.'
}
& (Join-Path $PSScriptRoot 'build-winui.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'YolanLive.WinUI\YolanLive.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
& $CompilerPath /Q "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer\YolanLive.iss')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
