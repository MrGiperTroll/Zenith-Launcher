; =============================================================================
;  Zenith Launcher - Installer (Standard Modern, Light)
;  Inno Setup 6 script. Build:
;      powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
; -----------------------------------------------------------------------------
;  Install location : {localappdata}\Programs\ZenithLauncher
;  Shortcuts        : Desktop + Start Menu (app icon)
;  Uninstall        : Standard Add/Remove Programs registration (Uninstall.exe)
; =============================================================================

#define MyAppName "Zenith Launcher"
#define MyAppVersion "1.0.4"
#define MyAppPublisher "Zenith"
#define MyAppExeName "Zenith Launcher.exe"
; Fixed installer GUID (do not change once published, used for clean removal)
#define MyAppId "{{8F3B4A2C-9D71-4E1B-A5C0-7D6E9F1B2C3D}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/MrGiperTroll/Zenith-Launcher
AppSupportURL=https://github.com/MrGiperTroll/Zenith-Launcher
AppUpdatesURL=https://github.com/MrGiperTroll/Zenith-Launcher

DefaultDirName={localappdata}\Programs\ZenithLauncher
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\Output
OutputBaseFilename=ZenithLauncher_Setup_v{#MyAppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Uninstallable=yes
CloseApplications=yes
RestartApplications=no
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "startmenu";  Description: "Create a &Start Menu shortcut"

[Files]
Source: "..\publish\win-x64-singlefile\Zenith Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\win-x64-singlefile\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\Assets\app.ico"
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu; IconFilename: "{app}\Assets\app.ico"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Name: "{app}"; Type: dirifempty
