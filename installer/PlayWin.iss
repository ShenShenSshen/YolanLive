#ifndef AppVersion
  #define AppVersion "1.0.7"
#endif
#ifdef TestUserDataDir
  #define BrandName "游窗-test"
  #define InstallerId "{{37BB6116-23DA-47D6-A658-3118D9587CA2}"
  #define UserDataDir TestUserDataDir
  #define TestSuffix "-test"
#else
  #define BrandName "游窗"
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
AppPublisherURL=https://github.com/ShenShenSshen/PlayWin
AppSupportURL=https://github.com/ShenShenSshen/PlayWin/issues
DefaultDirName={localappdata}\Programs\PlayWin
DefaultGroupName={#BrandName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.18362
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=PlayWin-{#AppVersion}-x64-Setup{#TestSuffix}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Assets\PlayWin.ico
UninstallDisplayName={#BrandName}
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
UsePreviousTasks=no
SetupIconFile=..\PlayWin.WinUI\Assets\PlayWin.ico

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\artifacts\PlayWin-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#BrandName}"; Filename: "{app}\PlayWin.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\PlayWin.ico"
Name: "{userdesktop}\{#BrandName}"; Filename: "{app}\PlayWin.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\PlayWin.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\PlayWin.exe"; Description: "启动游窗"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{#UserDataDir}"
