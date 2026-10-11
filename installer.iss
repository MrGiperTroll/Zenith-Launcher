; ============================================
; Zenith Launcher — Inno Setup Installer Script
; ============================================
; Requires: Inno Setup 6.7+ (built-in dark wizard via WizardStyle=dark)
; Compile:  powershell -ExecutionPolicy Bypass -File build-installer.ps1
;           (reads <Version> from CustomMcLauncher.csproj automatically;
;            override: build-installer.ps1 -Version 1.2.3)
; Output:   Output\ZenithLauncher_Setup_v<version>.exe
; ============================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.4"
#endif
#define MyAppName      "Zenith Launcher"
#define MyAppPublisher "Zenith Launcher Team"
#define MyAppURL       "https://github.com/MrGiperTroll/Zenith-Launcher"
#define MyAppExeName   "Zenith Launcher.exe"

[Setup]
AppId={{B4E7A3F1-7D2C-4A8E-9F0B-3C6D5E8A1F2B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=ZenithLauncher_Setup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=dark
WizardSizePercent=110
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
DisableDirPage=no
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
SetupIconFile=Assets\app.ico
CloseApplications=force
CloseApplicationsFilter=Zenith Launcher.exe
RestartApplications=yes

[Languages]
Name: "english";    MessagesFile: "compiler:Default.isl"
Name: "russian";     MessagesFile: "compiler:Languages\Russian.isl"
Name: "german";      MessagesFile: "compiler:Languages\German.isl"
Name: "spanish";     MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french";      MessagesFile: "compiler:Languages\French.isl"
Name: "japanese";    MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "startmenuicon"; Description: "Create Start Menu shortcut"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; Staged by build-installer.ps1 (self-contained single-file publish + Assets).
; User data lives in %APPDATA%\.zenith (outside {app}) so reinstalls/upgrades
; never touch settings, instances or accounts.
Source: "publish\setup-stage\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Messages]
WelcomeLabel1=Welcome to the {#MyAppName} Setup
WelcomeLabel2=This will install {#MyAppName} v{#MyAppVersion} on your computer.%n%nZenith Launcher is a modern Minecraft launcher with built-in mod management.%n%nClick Next to continue.
FinishedHeadingLabel=Setup Complete
FinishedLabel=Setup has successfully installed {#MyAppName}.%n%nClick Finish to close this wizard.
