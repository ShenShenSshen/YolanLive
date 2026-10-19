param([string]$CompilerPath)
$installerCheckScriptRoot = $PSScriptRoot
& (Join-Path $installerCheckScriptRoot '..\with-build-cache.ps1') {
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $installerCheckScriptRoot '..'))
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'PlayWin.WinUI\PlayWin.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$setup = Join-Path $root "artifacts\PlayWin-$version-x64-Setup.exe"
if ((Get-Item -LiteralPath $setup).VersionInfo.ProductVersion.Trim() -ne $version) { throw 'Setup executable version does not match project.' }
$testRoot = Join-Path $root 'artifacts\installer-test'
$installDir = Join-Path $testRoot 'app'
if (-not $installDir.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Test install directory outside workspace.' }
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{37BB6116-23DA-47D6-A658-3118D9587CA2}_is1'
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) '游窗-test.lnk'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) '游窗-test.lnk'
if ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $menu) -or (Test-Path -LiteralPath $desktop)) { throw 'An existing test installation is present.' }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$testData = Join-Path $testRoot 'userdata'
if (-not $CompilerPath) { $CompilerPath = Join-Path $root 'artifacts\tools\InnoSetup\ISCC.exe' }
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw 'Pass -CompilerPath to compile the isolated uninstall test package.' }
& $CompilerPath /Q "/DAppVersion=$version" "/DTestUserDataDir=$testData" (Join-Path $root 'installer\PlayWin.iss')
if ($LASTEXITCODE -ne 0) { throw 'Isolated installer compilation failed.' }
$setup = Join-Path $root "artifacts\PlayWin-$version-x64-Setup-test.exe"
$data = Join-Path $env:TEMP ('PlayWin-smoke-test\installer-' + $version + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $data -Force | Out-Null
$previousSmokeProfile = $env:PLAYWIN_SMOKE_PROFILE
$env:PLAYWIN_SMOKE_PROFILE = $data
function Run-Setup([string]$path, [string[]]$arguments, [int]$timeoutMilliseconds = 60000) {
    $process = Start-Process -FilePath $path -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($timeoutMilliseconds)) { Stop-Process -Id $process.Id; throw 'Installer test process exceeded its timeout.' }
    if ($process.ExitCode -ne 0) { throw "Installer test process failed: $($process.ExitCode)." }
}
function Remove-TestInstallation {
    if (-not (Test-Path -LiteralPath $registry)) { return }
    $entry = Get-ItemProperty -LiteralPath $registry
    $uninstaller = $entry.UninstallString.Trim('"')
    if ([IO.Path]::GetDirectoryName($uninstaller) -ne $installDir) { throw 'Uninstaller is outside the test install directory.' }
    Run-Setup $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
    for ($wait = 0; $wait -lt 100; $wait++) {
        if (-not (Test-Path -LiteralPath $registry) -and -not (Test-Path -LiteralPath $menu) -and -not (Test-Path -LiteralPath $desktop) -and
            -not (Get-ChildItem -LiteralPath $installDir -File -Recurse -ErrorAction SilentlyContinue)) { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'Uninstaller cleanup did not finish before the next test installation.'
}
$flags = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $installDir + '"'))
try {
    if ($PreviousInstaller) { throw 'PreviousInstaller must not be used with an isolated installation identity.' }
    Run-Setup $setup ($flags + '/MERGETASKS=!desktopicon')
    if (Test-Path -LiteralPath $desktop) { throw 'Explicitly declining the desktop shortcut was ignored.' }
    Remove-TestInstallation
    'PASS user may decline the desktop shortcut'
    Run-Setup $setup $flags
    $exe = Join-Path $installDir 'PlayWin.exe'
    if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $menu) -or -not (Test-Path -LiteralPath $desktop)) { throw 'Installed executable or shortcuts missing.' }
    $installed = Get-ItemProperty -LiteralPath $registry
    if ($installed.DisplayVersion -ne $version -or $installed.DisplayName -ne '游窗-test') { throw 'Installed name or version does not match project.' }
    if (-not (Get-Item -LiteralPath $exe).VersionInfo.FileVersion.StartsWith($version + '.')) { throw 'Executable version does not match installer.' }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($menu)
    if ($shortcut.TargetPath -ne $exe) { throw 'Start menu shortcut target is incorrect.' }
    $icon = Join-Path $installDir 'Assets\PlayWin.ico'
    $desktopShortcut = $shell.CreateShortcut($desktop)
    if ($desktopShortcut.TargetPath -ne $exe -or $desktopShortcut.IconLocation -ne ($icon + ',0')) { throw 'Desktop shortcut does not use the PlayWin B icon.' }
    if (-not (Test-Path -LiteralPath $icon)) { throw 'PlayWin B icon was not installed.' }
    'PASS current-user install, version and default desktop shortcut with PlayWin B icon'
    New-Item -ItemType Directory -Path (Join-Path $testData 'DouyinWebView\Default') -Force | Out-Null
    foreach ($name in @('settings.json','PlayWin.db','history.db','application.log','DouyinWebView\Default\Cookies')) {
        Set-Content -LiteralPath (Join-Path $testData $name) -Value 'fake uninstall test data, not a real session'
    }
    $readme = Join-Path $installDir 'README.md'
    $expected = (Get-FileHash -LiteralPath $readme -Algorithm SHA256).Hash
    Add-Content -LiteralPath $readme -Value 'installer upgrade test marker'
    Run-Setup $setup $flags
    if ((Get-FileHash -LiteralPath $readme -Algorithm SHA256).Hash -ne $expected) { throw 'Upgrade did not replace program files.' }
    if (-not (Test-Path -LiteralPath (Join-Path $testData 'settings.json'))) { throw 'Upgrade unexpectedly cleared user data.' }
    'PASS reinstall restores program files without duplicate installation'
    Run-Setup $exe @('--smoke-test') 180000
    $report = Get-Content -LiteralPath (Join-Path $data 'smoke-test.log')
    $passedChecks = ($report | Where-Object { $_ -match '^PASS' }).Count
    if (($report | Where-Object { $_ -match '^FAIL' }) -or $passedChecks -lt 94 -or -not ($report -contains 'PASS PlayWin metadata and clean title bar') -or -not ($report -contains 'PASS PlayWin filenames and runtime dependency references')) { throw 'Installed application smoke or PlayWin branding checks failed.' }
    'PASS installed application: ' + $passedChecks + ' smoke checks including PlayWin branding'
}
finally {
    $env:PLAYWIN_SMOKE_PROFILE = $previousSmokeProfile
    Remove-TestInstallation
}
if ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $menu) -or (Test-Path -LiteralPath $desktop) -or (Test-Path -LiteralPath (Join-Path $installDir 'PlayWin.exe'))) { throw 'Uninstall left the application, registry or shortcuts behind.' }
if (Test-Path -LiteralPath $testData) { throw 'Uninstall retained test user data or browser cache.' }

'PASS uninstall removes program, shortcuts, all user data and browser cache; real user data untouched by isolated testing'
}
