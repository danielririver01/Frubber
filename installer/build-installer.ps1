# Frubber POS — construir el instalador (JHO-54)
# Requiere: .NET 10 SDK y Inno Setup 6 (https://jrsoftware.org/isdl.php).
# Uso:  powershell -File installer\build-installer.ps1 [-Version 1.0.0]
# El instalador queda en installer\output\FrubberPOS-Setup-<version>.exe

param([string]$Version = "1.0.0")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
if (-not (Test-Path (Join-Path $root "FrubberPOS.sln"))) { $root = (Get-Location).Path }

Write-Host "1/3 Publicando binario autocontenido x64..."
dotnet publish (Join-Path $root "src\FrubberPOS.Desktop\FrubberPOS.Desktop.csproj") `
    -c Release -r win-x64 --self-contained `
    -o (Join-Path $root "artifacts\publish") `
    -p:Version=$Version

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
}
if (-not $iscc) { throw "Inno Setup 6 no esta instalado (falta ISCC.exe)." }

Write-Host "2/3 Compilando instalador con Inno Setup..."
& $iscc "/DMyAppVersion=$Version" (Join-Path $root "installer\FrubberPOS.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC fallo con codigo $LASTEXITCODE." }

Write-Host "3/3 Listo: $root\installer\output\FrubberPOS-Setup-$Version.exe"
