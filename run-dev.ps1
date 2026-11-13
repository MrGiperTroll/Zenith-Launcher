[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$LauncherArgs
)

$ErrorActionPreference = "Stop"

Write-Host "Building Zenith Launcher (Debug)..." -ForegroundColor Cyan
dotnet build (Join-Path $PSScriptRoot "CustomMcLauncher.csproj")

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$exePath = Join-Path $PSScriptRoot "bin\Debug\net10.0-windows\Zenith Launcher.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found at: $exePath"
    exit 1
}

Write-Host "Launching: $exePath" -ForegroundColor Green
Start-Process -FilePath $exePath -ArgumentList $LauncherArgs -WorkingDirectory (Split-Path $exePath -Parent)
