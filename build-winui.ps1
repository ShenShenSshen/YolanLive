& (Join-Path $PSScriptRoot 'with-build-cache.ps1') {
$ErrorActionPreference = 'Stop'
dotnet publish (Join-Path $PSScriptRoot 'YolanLive.WinUI\YolanLive.csproj') -c Release -p:Platform=x64 -o (Join-Path $PSScriptRoot 'artifacts\YolanLive-win-x64') --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'YolanLive.WinUI\README.md') -Destination (Join-Path $PSScriptRoot 'artifacts\YolanLive-win-x64\README.md') -Force
}
