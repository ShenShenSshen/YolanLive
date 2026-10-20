#ifndef AppVersion
  #define AppVersion "1.0.9"
#endif
#ifdef TestUserDataDir
  #define BrandName "游岚-test"
  #define LegacyBrandName "游窗-test"
  #define InstallerId "{{37BB6116-23DA-47D6-A658-3118D9587CA2}"
  #define UserDataDir TestUserDataDir
  #define TestSuffix "-test"
#else
  #define BrandName "游岚"
  #define LegacyBrandName "游窗"
  #define InstallerId "{{E2F869D5-6D37-4AA4-B932-5B782DDE2F17}"
  #define UserDataDir "{localappdata}\PlayWin"
  #define TestSuffix ""
#endif

[Setup]
AppId={#InstallerId}
AppName={#BrandName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}
AppPublisher=ShenShenSshen
AppPublisherURL=https://github.com/ShenShenSshen/YolanLive
AppSupportURL=https://github.com/ShenShenSshen/YolanLive/issues
DefaultDirName={localappdata}\Programs\YolanLive
DefaultGroupName={#BrandName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.18362
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=YolanLive-{#AppVersion}-x64-Setup{#TestSuffix}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Assets\YolanLive.ico
UninstallDisplayName={#BrandName}
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
UsePreviousTasks=no
SetupIconFile=..\YolanLive.WinUI\Assets\YolanLive.ico

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

; Preserve AppId and legacy data directory for in-place upgrades.
; Delete only obsolete files and shortcuts owned by the previous application.
[InstallDelete]
Type: files; Name: "{app}\PlayWin.exe"
Type: files; Name: "{app}\PlayWin.dll"
Type: files; Name: "{app}\PlayWin.Core.dll"
Type: files; Name: "{app}\PlayWin.pri"
Type: files; Name: "{app}\PlayWin.deps.json"
Type: files; Name: "{app}\PlayWin.runtimeconfig.json"
Type: files; Name: "{app}\PlayWin.pdb"
Type: files; Name: "{app}\PlayWin.Core.pdb"
Type: files; Name: "{app}\Assets\PlayWin.ico"
Type: files; Name: "{app}\Assets\PlayWin.png"
Type: files; Name: "{app}\Assets\PlayWin.svg"
Type: files; Name: "{userprograms}\{#LegacyBrandName}.lnk"
Type: files; Name: "{userdesktop}\{#LegacyBrandName}.lnk"

[Files]
Source: "..\artifacts\YolanLive-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#BrandName}"; Filename: "{app}\YolanLive.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\YolanLive.ico"
Name: "{userdesktop}\{#BrandName}"; Filename: "{app}\YolanLive.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\YolanLive.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\YolanLive.exe"; Description: "启动游岚"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{#UserDataDir}"
