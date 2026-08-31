# Zenith Launcher - Release / Installer

Prepares a self-contained, single-file `win-x64` Release build and packages it
into a Windows installer via **Inno Setup 6** using the clean, stock **Modern**
light wizard (no custom skins, no external theming DLLs).

## Prerequisites

- .NET SDK 10
- **Inno Setup 6** (the build script auto-detects `ISCC.exe`, e.g. from
  `%LOCALAPPDATA%\Programs\Inno Setup 6`). Install with:
  `winget install --id JRSoftware.InnoSetup --exact`

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
```

What it does:

1. `dotnet publish` → `publish\win-x64-singlefile\` (self-contained, single-file,
   x64, via `Properties\PublishProfiles\win-x64-singlefile.pubxml`). The published
   app ships as one `Zenith Launcher.exe` plus the `Assets\` runtime folder.
2. Compiles the installer → `Output\ZenithLauncher_Setup_v1.0.4.exe` (43&nbsp;MB).

## Installer behaviour (`installer\setup.iss`)

- **Clean standard Modern wizard** — `WizardStyle=modern`, native light background,
  standard Windows controls and system text. No custom styling code, VCL styles or
  wizard images.
- **Icon**: the launcher icon (`Assets\app.ico`) is used for both the installer file
  (`SetupIconFile`) and the app shortcuts. No image in the wizard header.
- **Destination**: `%LOCALAPPDATA%\Programs\ZenithLauncher`
  (no admin required, per-user install).
- **Shortcuts**: Desktop + Start Menu, using the app icon
  (`Assets\app.ico`).
- **Uninstall**: standard Add/Remove Programs registration
  (`{8F3B4A2C-9D71-4E1B-A5C0-7D6E9F1B2C3D}`), driven by the generated
  `unins000.exe`. User data in `%APPDATA%\.zenith` is intentionally kept.
- **Tasks**: "Desktop shortcut" (opt-in), "Start Menu shortcut" (default on).

## Files

| File | Purpose |
|------|---------|
| `setup.iss` | Inno Setup script (standard Modern install logic) |
| `Build-Installer.ps1` | One-shot publish &amp; installer build |

## Dry-run silent install / uninstall (CLI test)

```powershell
# install silently (with both shortcut tasks)
Output\ZenithLauncher_Setup_v1.0.4.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /MERGETASKS=desktopicon,startmenu

# uninstall silently
"%LOCALAPPDATA%\Programs\ZenithLauncher\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```
