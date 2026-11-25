; =============================================================================
;  Zenith Launcher - Installer (Standard Modern, Light)
;  Inno Setup 6 script. Build:
;      powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
; -----------------------------------------------------------------------------
;  Install location : {localappdata}\Zenith Launcher
;  Shortcuts        : Desktop + Start Menu (app icon)
;  Uninstall        : Standard Add/Remove Programs registration (Uninstall.exe)
; =============================================================================

#define MyAppName "Zenith Launcher"
#ifndef MyAppVersion
  #define MyAppVersion "1.1.1"
#endif
#define MyAppPublisher "Zenith"
#define MyAppExeName "Zenith Launcher.exe"
; Matches published v1.1.0 installer AppId for clean in-place upgrades
#define MyAppId "{{B4E7A3F1-7D2C-4A8E-9F0B-3C6D5E8A1F2B}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/MrGiperTroll/Zenith-Launcher
AppSupportURL=https://github.com/MrGiperTroll/Zenith-Launcher
AppUpdatesURL=https://github.com/MrGiperTroll/Zenith-Launcher

DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\Output
OutputBaseFilename=ZenithLauncher_Setup_v{#MyAppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
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
Source: "..\publish\win-x64-singlefile\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu; IconFilename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Name: "{app}"; Type: dirifempty
