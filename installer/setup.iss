; =============================================================================
;  Zenith Launcher - Installer (Dark Theme)
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
WizardImageFile=assets\wizard-left.bmp,assets\wizard-small.bmp
WizardSmallImageFile=assets\wizard-small.bmp
; Dark theming needs a recent Windows 10 (1809+ has uxtheme dark mode APIs)
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Service the built-in .NET app data unmodified by uninstall (user data stays):
Uninstallable=yes
CloseApplications=yes
RestartApplications=no
; Application does not need admin - keep it simple
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "startmenu";  Description: "Create a &Start Menu shortcut"

[Files]
Source: "..\publish\win-x64-singlefile\Zenith Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\win-x64-singlefile\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: .pdb debug symbol files are intentionally excluded from the installer.

[Icons]
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\Assets\app.ico"
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu; IconFilename: "{app}\Assets\app.ico"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove any leftovers created by the app inside its own install dir
Name: "{app}"; Type: dirifempty

[Code]
// ============================================================================
//  Dark Theme for the Inno Setup wizard.
//  Implemented with pure Inno color theming (no undocumented DLL imports,
//  no fragile pointer casts) so it can never crash the installer.
//  The wizard body + text are dark with green accents, matching the launcher.
//  (The OS caption bar follows the active Windows theme, as is standard.)
// ============================================================================
const
  // Launcher palette (BGR)
  cBg     = $1A110D;   // RGB(13,17,26)   CardBrush
  cText   = $FFFFFF;   // white
  cAccent = $81B910;   // RGB(16,185,129) AccentBrush

var
  DarkApplied: Boolean;

// Recursively apply dark colors to the wizard control tree
procedure ApplyDark(Control: TObject);
var
  i: Integer;
begin
  if Control is TForm then
    TForm(Control).Color := cBg;

  if Control is TLabel then
    TLabel(Control).Font.Color := cText
  else if Control is TNewStaticText then
    TNewStaticText(Control).Font.Color := cText
  else if Control is TNewCheckBox then
    TNewCheckBox(Control).Font.Color := cText
  else if Control is TNewRadioButton then
    TNewRadioButton(Control).Font.Color := cText
  else if Control is TNewEdit then begin
    TNewEdit(Control).Color := cBg;
    TNewEdit(Control).Font.Color := cText;
  end;

  // Walk children of any TWinControl / container
  if Control is TWinControl then begin
    for i := 0 to TWinControl(Control).ControlCount - 1 do
      ApplyDark(TWinControl(Control).Controls[i]);
  end;
end;

procedure InitializeWizard();
begin
  DarkApplied := True;
  ApplyDark(WizardForm);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // Re-apply on every page switch so newly-created controls stay themed
  if DarkApplied then
    ApplyDark(WizardForm);
end;

procedure InitializeUninstallProgressForm();
begin
  ApplyDark(UninstallProgressForm);
end;
