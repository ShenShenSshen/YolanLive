param([string]$CompilerPath, [string]$PreviousInstaller)
$installerCheckScriptRoot = $PSScriptRoot
& (Join-Path $installerCheckScriptRoot '..\with-build-cache.ps1') {
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $installerCheckScriptRoot '..'))
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'YolanLive.WinUI\YolanLive.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$setup = Join-Path $root "artifacts\YolanLive-$version-x64-Setup.exe"
if ((Get-Item -LiteralPath $setup).VersionInfo.ProductVersion.Trim() -ne $version) { throw 'Setup executable version does not match project.' }
$testRoot = Join-Path $root 'artifacts\installer-test'
$installDir = Join-Path $testRoot 'app'
if (-not $installDir.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Test install directory outside workspace.' }
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{37BB6116-23DA-47D6-A658-3118D9587CA2}_is1'
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) '游岚-test.lnk'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) '游岚-test.lnk'
if ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $menu) -or (Test-Path -LiteralPath $desktop)) { throw 'An existing test installation is present.' }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$testData = Join-Path $testRoot 'userdata'
if (-not $CompilerPath) { $CompilerPath = Join-Path $root 'artifacts\tools\InnoSetup\ISCC.exe' }
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw 'Pass -CompilerPath to compile the isolated uninstall test package.' }
& $CompilerPath /Q "/DAppVersion=$version" "/DTestUserDataDir=$testData" (Join-Path $root 'installer\YolanLive.iss')
if ($LASTEXITCODE -ne 0) { throw 'Isolated installer compilation failed.' }
$setup = Join-Path $root "artifacts\YolanLive-$version-x64-Setup-test.exe"
$data = Join-Path $env:TEMP ('YolanLive-smoke-test\installer-' + $version + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $data -Force | Out-Null
$previousSmokeProfile = $env:YOLANLIVE_SMOKE_PROFILE
$env:YOLANLIVE_SMOKE_PROFILE = $data
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
    if ($PreviousInstaller) {
        $previous = [IO.Path]::GetFullPath($PreviousInstaller)
        $allowedPrevious = Join-Path $root 'artifacts\PlayWin-1.0.7-x64-Setup-test.exe'
        if ($previous -ne $allowedPrevious -or (Get-Item -LiteralPath $previous).VersionInfo.ProductVersion.Trim() -ne '1.0.7') { throw 'Only the existing isolated v1.0.7 test installer is permitted.' }
        $legacyMenu = Join-Path ([Environment]::GetFolderPath('Programs')) '游窗-test.lnk'
        $legacyDesktop = Join-Path ([Environment]::GetFolderPath('Desktop')) '游窗-test.lnk'
        if ((Test-Path -LiteralPath $legacyMenu) -or (Test-Path -LiteralPath $legacyDesktop)) { throw 'Legacy test shortcuts already exist.' }
        Run-Setup $previous $flags
        $legacyEntry = Get-ItemProperty -LiteralPath $registry
        if ($legacyEntry.DisplayName -ne '游窗-test' -or $legacyEntry.DisplayVersion -ne '1.0.7') { throw 'Previous installer identity mismatch.' }
        New-Item -ItemType Directory -Path (Join-Path $testData 'DouyinWebView\Default') -Force | Out-Null
        foreach ($name in @('settings.json','PlayWin.db','DouyinWebView\Default\Cookies')) {
            Set-Content -LiteralPath (Join-Path $testData $name) -Value 'isolated legacy upgrade marker'
        }
        $before = @(Get-ChildItem -LiteralPath $testData -File -Recurse | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName).Hash })
        # No /DIR: the unchanged AppId must recover the old installation path.
        Run-Setup $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
        $renamedEntry = Get-ItemProperty -LiteralPath $registry
        if ($renamedEntry.DisplayName -ne '游岚-test' -or $renamedEntry.DisplayVersion -ne $version) { throw 'Upgrade did not reuse the existing installation identity.' }
        $after = @(Get-ChildItem -LiteralPath $testData -File -Recurse | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName).Hash })
        if (@(Compare-Object $before $after).Count -ne 0) { throw 'Rename upgrade changed legacy data.' }
        if ((Test-Path -LiteralPath (Join-Path $installDir 'PlayWin.exe')) -or (Test-Path -LiteralPath (Join-Path $installDir 'PlayWin.dll')) -or (Test-Path -LiteralPath $legacyMenu) -or (Test-Path -LiteralPath $legacyDesktop)) { throw 'Rename upgrade left obsolete executable or shortcuts.' }
        if (-not (Test-Path -LiteralPath (Join-Path $installDir 'YolanLive.exe')) -or -not (Test-Path -LiteralPath $menu) -or -not (Test-Path -LiteralPath $desktop)) { throw 'Rename upgrade did not replace entry points.' }
        'PASS v1.0.7 rename upgrade preserves legacy data and replaces executable and shortcuts'
        Remove-TestInstallation
    }
    Run-Setup $setup ($flags + '/MERGETASKS=!desktopicon')
    if (Test-Path -LiteralPath $desktop) { throw 'Explicitly declining the desktop shortcut was ignored.' }
    Remove-TestInstallation
    'PASS user may decline the desktop shortcut'
    Run-Setup $setup $flags
    $exe = Join-Path $installDir 'YolanLive.exe'
    if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $menu) -or -not (Test-Path -LiteralPath $desktop)) { throw 'Installed executable or shortcuts missing.' }
    $installed = Get-ItemProperty -LiteralPath $registry
    if ($installed.DisplayVersion -ne $version -or $installed.DisplayName -ne '游岚-test') { throw 'Installed name or version does not match project.' }
    if (-not (Get-Item -LiteralPath $exe).VersionInfo.FileVersion.StartsWith($version + '.')) { throw 'Executable version does not match installer.' }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($menu)
    if ($shortcut.TargetPath -ne $exe) { throw 'Start menu shortcut target is incorrect.' }
    $icon = Join-Path $installDir "Assets\YolanLive-$version.ico"
    $desktopShortcut = $shell.CreateShortcut($desktop)
    if ($desktopShortcut.TargetPath -ne $exe -or $desktopShortcut.IconLocation -ne ($icon + ',0')) { throw 'Desktop shortcut does not use the YolanLive D1 sail icon.' }
    if (-not (Test-Path -LiteralPath $icon)) { throw 'YolanLive D1 sail icon was not installed.' }
    if ((Get-FileHash -LiteralPath $icon).Hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'YolanLive.WinUI\Assets\YolanLive.ico')).Hash) { throw 'Installed shortcut icon differs from the current application icon.' }
    'PASS current-user install, version and default desktop shortcut with YolanLive D1 sail icon'
    New-Item -ItemType Directory -Path (Join-Path $testData 'DouyinWebView\Default') -Force | Out-Null
    foreach ($name in @('settings.json','PlayWin.db','history.db','application.log','DouyinWebView\Default\Cookies')) {
        Set-Content -LiteralPath (Join-Path $testData $name) -Value 'fake uninstall test data, not a real session'
    }
    $readme = Join-Path $installDir 'README.md'
    $expected = (Get-FileHash -LiteralPath $readme -Algorithm SHA256).Hash
    Add-Content -LiteralPath $readme -Value 'installer upgrade test marker'
    # Simulate shortcuts left by an older installer using the unchanged icon path.
    foreach ($link in @($menu, $desktop)) {
        $legacyShortcut = $shell.CreateShortcut($link)
        $legacyShortcut.IconLocation = (Join-Path $installDir 'Assets\YolanLive.ico') + ',0'
        $legacyShortcut.Save()
    }
    Run-Setup $setup $flags
    foreach ($link in @($menu, $desktop)) {
        if ($shell.CreateShortcut($link).IconLocation -ne ($icon + ',0')) { throw 'Upgrade retained the cached legacy shortcut icon path.' }
    }
    if ($installed.DisplayIcon -ne $icon) { throw 'Installed application entry does not use the current version icon.' }
    'PASS upgrade replaces legacy shortcut icon paths with the current version icon'
    if ((Get-FileHash -LiteralPath $readme -Algorithm SHA256).Hash -ne $expected) { throw 'Upgrade did not replace program files.' }
    if (-not (Test-Path -LiteralPath (Join-Path $testData 'settings.json'))) { throw 'Upgrade unexpectedly cleared user data.' }
    'PASS reinstall restores program files without duplicate installation'
    Run-Setup $exe @('--smoke-test') 180000
    $report = Get-Content -LiteralPath (Join-Path $data 'smoke-test.log')
    $passedChecks = ($report | Where-Object { $_ -match '^PASS' }).Count
    if (($report | Where-Object { $_ -match '^FAIL' }) -or $passedChecks -lt 94 -or -not ($report -contains 'PASS YolanLive metadata and clean title bar') -or -not ($report -contains 'PASS YolanLive filenames and runtime dependency references')) { throw 'Installed application smoke or YolanLive branding checks failed.' }
    'PASS installed application: ' + $passedChecks + ' smoke checks including YolanLive branding'
}
finally {
    $env:YOLANLIVE_SMOKE_PROFILE = $previousSmokeProfile
    Remove-TestInstallation
}
if ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $menu) -or (Test-Path -LiteralPath $desktop) -or (Test-Path -LiteralPath (Join-Path $installDir 'YolanLive.exe'))) { throw 'Uninstall left the application, registry or shortcuts behind.' }
if (Test-Path -LiteralPath $testData) { throw 'Uninstall retained test user data or browser cache.' }

'PASS uninstall removes program, shortcuts, all user data and browser cache; real user data untouched by isolated testing'
}
