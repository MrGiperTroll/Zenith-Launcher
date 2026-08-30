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
//  Sets WizardForm.Color directly (not through recursive TForm check, since
//  WizardForm is TWizardForm which does not match "is TForm" in Inno Pascal).
//  Then recursively applies light text to all child labels/controls.
// ============================================================================
const
  cBg     = $1A110D;   // RGB(13,17,26)   CardBrush
  cText   = $FFFFFF;   // white

var
  DarkApplied: Boolean;

procedure ApplyDarkControls(Parent: TObject);
var
  i: Integer;
  Ctrl: TObject;
begin
  if not (Parent is TWinControl) then Exit;

  for i := 0 to TWinControl(Parent).ControlCount - 1 do begin
    Ctrl := TWinControl(Parent).Controls[i];

    if Ctrl is TLabel then
      TLabel(Ctrl).Font.Color := cText
    else if Ctrl is TNewStaticText then
      TNewStaticText(Ctrl).Font.Color := cText
    else if Ctrl is TNewCheckBox then
      TNewCheckBox(Ctrl).Font.Color := cText
    else if Ctrl is TNewRadioButton then
      TNewRadioButton(Ctrl).Font.Color := cText
    else if Ctrl is TNewEdit then begin
      TNewEdit(Ctrl).Color := cBg;
      TNewEdit(Ctrl).Font.Color := cText;
    end;

    ApplyDarkControls(Ctrl);
  end;
end;

procedure InitializeWizard();
begin
  DarkApplied := True;
  // Set dark background directly — this is the key line that was missing
  WizardForm.Color := cBg;
  ApplyDarkControls(WizardForm);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if DarkApplied then begin
    WizardForm.Color := cBg;
    ApplyDarkControls(WizardForm);
  end;
end;

procedure InitializeUninstallProgressForm();
begin
  UninstallProgressForm.Color := cBg;
  ApplyDarkControls(UninstallProgressForm);
end;
