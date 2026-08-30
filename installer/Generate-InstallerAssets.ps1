# Generates dark-themed BMP assets for the Zenith Launcher Inno Setup installer.
# Reproducible: run `powershell -ExecutionPolicy Bypass -File installer/Generate-InstallerAssets.ps1`
# Colors match the launcher palette (AccentBrush #10B981, CardBrush #0D111A, SurfaceBrush #0A0D15).

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$outDir = Join-Path $PSScriptRoot 'assets'
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$bg     = [System.Drawing.Color]::FromArgb(13, 17, 26)      # CardBrush #0D111A
$bgTop  = [System.Drawing.Color]::FromArgb(10, 13, 21)      # SurfaceBrush #0A0D15
$accent = [System.Drawing.Color]::FromArgb(16, 185, 129)    # AccentBrush #10B981
$white  = [System.Drawing.Color]::FromArgb(255, 255, 255)

# ---- Left panel 164x314 (modern wizard left image) ----
$left = New-Object System.Drawing.Bitmap 164, 314
$lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point 0,0),
    (New-Object System.Drawing.Point 164,314),
    $bgTop, $bg)
$g = [System.Drawing.Graphics]::FromImage($left)
$g.Clear($bgTop)
$g.FillRectangle($lg, 0, 0, 164, 314)

# Accent bar along the left edge
$bar = New-Object System.Drawing.SolidBrush $accent
$g.FillRectangle($bar, 0, 0, 5, 314)

# Brand text: "ZENITH" with green "LAUNCHER" below
$brandFont = New-Object System.Drawing.Font('Segoe UI', 22, [System.Drawing.FontStyle]::Bold)
$subFont   = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)
$textBrush = New-Object System.Drawing.SolidBrush $white
$accentBrush = New-Object System.Drawing.SolidBrush $accent
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

$g.DrawString('ZENITH', $brandFont, $textBrush, (New-Object System.Drawing.RectangleF 12, 96, 140, 44), $sf)
$g.DrawString('LAUNCHER', $subFont, $accentBrush, (New-Object System.Drawing.RectangleF 12, 138, 140, 30), $sf)

$left.Save((Join-Path $outDir 'wizard-left.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)

# ---- Small top-right 55x55 (modern wizard small image / license area) ----
$small = New-Object System.Drawing.Bitmap 55, 55
$g2 = [System.Drawing.Graphics]::FromImage($small)
$g2.Clear($bg)
$g2.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$zFont = New-Object System.Drawing.Font('Segoe UI', 20, [System.Drawing.FontStyle]::Bold)
$g2.DrawString('Z', $zFont, $accentBrush, (New-Object System.Drawing.RectangleF 0, 6, 55, 42), $sf)
$small.Save((Join-Path $outDir 'wizard-small.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)

# ---- Top banner 386x58 (used as WizardImageFile "top" banner if desired) ----
$banner = New-Object System.Drawing.Bitmap 386, 58
$g3 = [System.Drawing.Graphics]::FromImage($banner)
$g3.Clear($bgTop)
$g3.FillRectangle((New-Object System.Drawing.SolidBrush $accent), 0, 0, 4, 58)
$g3.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g3.DrawString('Zenith Launcher', $subFont, $textBrush, (New-Object System.Drawing.RectangleF 12, 0, 370, 58), $sf)
$banner.Save((Join-Path $outDir 'banner.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)

$g.Dispose(); $g2.Dispose(); $g3.Dispose()
$left.Dispose(); $small.Dispose(); $banner.Dispose()

Get-ChildItem -LiteralPath $outDir | Select-Object Name, Length
Write-Output "Assets generated in $outDir"
