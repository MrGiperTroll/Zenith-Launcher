# =============================================================================
#  Zenith Launcher - Release installer build script
#  Steps:
#    1. Read <Version> from ZenithLauncher.csproj
#    2. Publish clean self-contained single-file win-x64 Release
#    3. Compile the Inno Setup installer -> Output\ZenithLauncher_Setup_v<Version>.exe
#
#  Usage:
#     powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1 [-Version 1.2.0]
# =============================================================================
param([string]$Version = "")
$ErrorActionPreference = 'Stop'

$root       = Split-Path -Parent $PSScriptRoot
$csproj     = Join-Path $root 'ZenithLauncher.csproj'
$iss        = Join-Path $PSScriptRoot 'setup.iss'
$publishDir = Join-Path $root 'publish\win-x64-singlefile'
$outputDir  = Join-Path $root 'Output'

if (-not $Version) {
    [xml]$projXml = Get-Content -LiteralPath $csproj
    $Version = ($projXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
}
if (-not $Version) { throw "Version not found in ZenithLauncher.csproj and -Version was not specified." }
$Version = $Version.Trim().TrimStart('v', 'V')

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
Write-Host "ISCC    : $iscc" -ForegroundColor Cyan
Write-Host "Version : $Version" -ForegroundColor Cyan

# --- 1. Publish single-file self-contained -----------------------------------
Write-Host "`n[1/2] Publishing single-file self-contained win-x64 Release (v$Version)..." -ForegroundColor Cyan
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
Push-Location $root
try {
    dotnet publish -c Release -p:PublishProfile=win-x64-singlefile
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
} finally { Pop-Location }

# --- 2. Compile the installer -------------------------------------------------
Write-Host "`n[2/2] Compiling installer -> Output\ZenithLauncher_Setup_v$Version.exe`n" -ForegroundColor Cyan
# ISCC uses its own working directory; run from the installer folder so relative
# Source paths in setup.iss resolve against the installer\ directory.
Push-Location $PSScriptRoot
try {
    & $iscc $iss "/DMyAppVersion=$Version"
    if ($LASTEXITCODE -ne 0) { throw "ISCC compilation failed (exit $LASTEXITCODE)" }
} finally { Pop-Location }

$exe = Join-Path $outputDir "ZenithLauncher_Setup_v$Version.exe"
if (Test-Path -LiteralPath $exe) {
    $mb = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 2)
    Write-Host "`nDONE: $exe ($mb MB)" -ForegroundColor Green
} else {
    throw "Expected output not found: $exe"
}
