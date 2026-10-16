#ifndef PublishDir
  #error PublishDir must point to a fresh self-contained publish.
#endif
#ifndef OutputDir
  #error OutputDir is required.
#endif
#ifndef BuildVersion
  #error BuildVersion is required.
#endif
#ifndef NumericVersion
  #error NumericVersion is required.
#endif
#ifndef RuntimeArch
  #define RuntimeArch "x64"
#endif
#ifdef TestInstaller
  #define ProductName "YATSS Installer Test"
  #define ProductId "YATSS.InstallerSmokeTest"
  #define OutputSuffix "-test"
#else
  #define ProductName "YATSS"
  #define ProductId "{{813FB496-F338-4321-B9B8-604D924B6409}"
  #define OutputSuffix ""
#endif

[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#BuildVersion}
AppPublisher=YATSS contributors
AppPublisherURL=https://github.com/tekrantz57/YATSS
AppSupportURL=https://github.com/tekrantz57/YATSS/issues
AppUpdatesURL=https://github.com/tekrantz57/YATSS/releases
VersionInfoVersion={#NumericVersion}
VersionInfoDescription={#ProductName} Setup
VersionInfoProductName={#ProductName}
DefaultDirName={localappdata}\Programs\{#ProductName}
DefaultGroupName={#ProductName}
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
MinVersion=10.0.19045
#if RuntimeArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
#endif
AppMutex=Local\YATSS.Desktop.Running
SetupMutex=Local\YATSS.Setup.Running
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
WizardStyle=modern
SetupIconFile=..\YATSSWin\YATSS\Assets\app.ico
UninstallDisplayIcon={app}\YATSSWin.exe
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=YATSS-{#BuildVersion}-win-{#RuntimeArch}-setup{#OutputSuffix}
Compression=lzma2
SolidCompression=yes

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\YATSSWin.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\{#ProductName}"; Filename: "{app}\YATSSWin.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\YATSSWin.exe"; Description: "Launch {#ProductName}"; Flags: nowait postinstall skipifsilent unchecked

; No user-data directories are installed or removed. Only installer-owned files
; are removed on uninstall; never add a recursive {app} or AppData deletion.
