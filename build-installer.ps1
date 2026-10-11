param([string]$Version = "")
$ErrorActionPreference = 'Stop'

# Builds the black-themed Inno Setup installer for Zenith Launcher.
#   powershell -ExecutionPolicy Bypass -File build-installer.ps1 [-Version 1.2.3]
# Version defaults to <Version> from CustomMcLauncher.csproj.

$root = $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }
Set-Location $root

if (-not $Version) {
    [xml]$xml = Get-Content (Join-Path $root 'CustomMcLauncher.csproj')
    $Version = $xml.Project.PropertyGroup.Version
}
if (-not $Version) { throw 'Version not found (csproj <Version> empty and -Version not given).' }
Write-Output "Building installer for v$Version ..."

$stage = Join-Path $root 'publish\setup-stage'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'CustomMcLauncher.csproj') -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o $stage --nologo -v q --property WarningLevel=0
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\iscc.exe'
if (-not (Test-Path $iscc)) { $iscc = 'C:\Program Files (x86)\Inno Setup 6\iscc.exe' }
if (-not (Test-Path $iscc)) { throw 'iscc.exe not found. Install Inno Setup 6.7+.' }

& $iscc (Join-Path $root 'installer.iss') "/DMyAppVersion=$Version"
if ($LASTEXITCODE -ne 0) { throw "iscc failed ($LASTEXITCODE)." }

$exe = Join-Path $root "Output\ZenithLauncher_Setup_v$Version.exe"
if (-not (Test-Path $exe)) { throw 'Expected output missing: ' + $exe }
Write-Output ('OK: ' + $exe + ' (' + [math]::Round((Get-Item $exe).Length / 1MB, 1) + ' MB)')
