# =============================================================================
#  Zenith Launcher - Release installer build script
#  Steps:
#    1. Publish self-contained single-file win-x64 Release
#    2. Generate/refresh dark installer assets (idempotent)
#    3. Compile the Inno Setup installer -> dist\ZenithLauncher-Setup.exe
#
#  Usage:
#     powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
# =============================================================================
$ErrorActionPreference = 'Stop'

$root     = Split-Path -Parent $PSScriptRoot
$iss      = Join-Path $PSScriptRoot 'setup.iss'
$distDir  = Join-Path $root 'dist'

# --- 0. Locate ISCC.exe ------------------------------------------------------
$iscc = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) { throw "ISCC.exe not found. Install Inno Setup 6 first." }
Write-Host "ISCC: $iscc" -ForegroundColor Cyan

# --- 1. Publish single-file self-contained -----------------------------------
Write-Host "`n[1/3] Publishing single-file self-contained win-x64 Release..." -ForegroundColor Cyan
Push-Location $root
try {
    dotnet publish -c Release -p:PublishProfile=win-x64-singlefile
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
} finally { Pop-Location }

# --- 2. Refresh dark installer assets ----------------------------------------
Write-Host "`n[2/3] Generating dark installer assets..." -ForegroundColor Cyan
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Generate-InstallerAssets.ps1')

# --- 3. Compile the installer -------------------------------------------------
if (-not (Test-Path -LiteralPath $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
Write-Host "`n[3/3] Compiling installer -> dist\ZenithLauncher-Setup.exe`n" -ForegroundColor Cyan
# ISCC uses its own working directory; run from the installer folder so relative
# Source paths in setup.iss resolve against the installer\ directory.
Push-Location $PSScriptRoot
try {
    & $iscc $iss
    if ($LASTEXITCODE -ne 0) { throw "ISCC compilation failed (exit $LASTEXITCODE)" }
} finally { Pop-Location }

$exe = Join-Path $distDir 'ZenithLauncher-Setup.exe'
if (Test-Path -LiteralPath $exe) {
    $mb = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 2)
    Write-Host "`nDONE: $exe ($mb MB)" -ForegroundColor Green
} else {
    throw "Expected output not found: $exe"
}
