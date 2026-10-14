# Frubber POS - genera los assets del instalador (JHO-54)
#  - assets\wizard-164.bmp  (banda izquierda de bienvenida/cierre)
#  - assets\wizard-55.bmp   (esquina superior de las paginas internas)
#  - assets\Licencia-FrubberPOS.rtf (contrato; regenerado desde Licencia-FrubberPOS.txt)
# Solo Windows PowerShell 5.1 (System.Drawing). Uso: powershell -File installer\make-assets.ps1
# NOTA: este archivo es ASCII a proposito (PS 5.1 sin BOM confunde tildes/enes).

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing | Out-Null

$installerDir = $PSScriptRoot
$root = Split-Path -Parent $installerDir
$assets = Join-Path $installerDir "assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null

function Save-Bmp([System.Drawing.Bitmap]$bmp, [string]$path) {
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
}

# Carpeta de diseno con la marca (Documentacion\Diseno de Interfaz...\iconos)
$uxDir = Get-ChildItem (Join-Path $root "Documentacion") -Directory |
    Where-Object { $_.Name.StartsWith("Dise") -and (Test-Path (Join-Path $_.FullName "iconos\frubber-pos-256.png")) } |
    Select-Object -First 1
if (-not $uxDir) { throw "No se encontro la carpeta de iconos de marca bajo Documentacion." }
$logoPath = Join-Path $uxDir.FullName "iconos\frubber-pos-256.png"

$logo = [System.Drawing.Image]::FromFile($logoPath)

# --- Banda izquierda de bienvenida: blanco, logo centrado, franja de marca abajo ---
$band = New-Object System.Drawing.Bitmap 164, 314
$band.SetResolution(96, 96)
$g = [System.Drawing.Graphics]::FromImage($band)
$g.Clear([System.Drawing.Color]::White)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$logoSize = 120
$g.DrawImage($logo, [int](($band.Width - $logoSize) / 2), 60, $logoSize, $logoSize)
$strip = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x0F, 0x76, 0x6E)) # verde de marca
$g.FillRectangle($strip, 0, $band.Height - 10, $band.Width, 10)
$g.Dispose(); $strip.Dispose()
Save-Bmp $band (Join-Path $assets "wizard-164.bmp")

# --- Imagen pequena (paginas internas): logo sobre blanco ---
$small = New-Object System.Drawing.Bitmap 55, 58
$small.SetResolution(96, 96)
$g2 = [System.Drawing.Graphics]::FromImage($small)
$g2.Clear([System.Drawing.Color]::White)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.DrawImage($logo, 4, 5, 48, 48)
$g2.Dispose()
Save-Bmp $small (Join-Path $assets "wizard-55.bmp")
$logo.Dispose()

# --- Contrato de licencia: Licencia-FrubberPOS.txt (UTF-8) -> RTF escapado a cp1252 ---
$txt = Join-Path $assets "Licencia-FrubberPOS.txt"
$rtf = Join-Path $assets "Licencia-FrubberPOS.rtf"
$lines = [System.IO.File]::ReadAllText($txt) -split "\r?\n"
$cp1252 = [System.Text.Encoding]::GetEncoding(1252)
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss\fcharset0 Segoe UI;}}")
[void]$sb.Append("{\colortbl;\red15\green118\blue110;}") # verde de marca
[void]$sb.Append("\viewkind4\uc1\pard\sa120\sl276\slmult1\fs20 ")
$first = $true
foreach ($raw in $lines) {
    $line = $raw.TrimEnd()
    if ($line -eq "") { continue } # el espaciado lo hace \sa120
    $escaped = ($cp1252.GetBytes($line) | ForEach-Object {
        if ($_ -gt 126) { "\'{0:x2}" -f $_ } elseif ("{}\".ToCharArray() -contains [char]$_) { "\" + [char]$_ } else { [char]$_ }
    }) -join ""
    if ($first) {
        [void]$sb.Append("\cf1\b\fs26 $escaped\b0\fs20\cf0\par ")
        $first = $false
    } elseif ($line -match '^\d+\.') {
        [void]$sb.Append("\pard\sa120\sl276\slmult1\b $escaped\b0\par ")
    } else {
        [void]$sb.Append("\pard\sa120\sl276\slmult1 $escaped\par ")
    }
}
[void]$sb.Append("}")
[System.IO.File]::WriteAllText($rtf, $sb.ToString(), [System.Text.Encoding]::ASCII)

Write-Host "Assets generados en $assets"
