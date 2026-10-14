# Frubber POS — construir el instalador WPF (JHO-93; el script Inno Setup
# heredado queda como respaldo junto a este mismo archivo).
# Requiere: .NET 10 SDK. NADA más (no necesita Inno Setup).
# Uso:  powershell -File installer\build-installer.ps1 [-Version 1.0.0]
# El instalador final queda en installer\output\FrubberPOS-Setup-<version>.exe
# (un solo .exe autocontenido: la app viaja embebida como payload.zip).

param(
    [string]$Version = "1.0.0",
    # Solo compila la app (artifacts/publish) sin re-generar el instalador.
    [switch]$AppOnly
)

$ErrorActionPreference = "Stop"
$installerDir = $PSScriptRoot
$project = Join-Path $installerDir "FrubberPOS.Installer\FrubberPOS.Installer.csproj"
$root = Split-Path -Parent $installerDir
if (-not (Test-Path (Join-Path $root "FrubberPOS.sln"))) { $root = (Get-Location).Path }

Write-Host "1/4 Publicando la app (autocontenida x64)..."
dotnet publish (Join-Path $root "src\FrubberPOS.Desktop\FrubberPOS.Desktop.csproj") `
    -c Release -r win-x64 --self-contained `
    -o (Join-Path $root "artifacts\publish") `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (app) fallo con codigo $LASTEXITCODE." }

if ($AppOnly) { exit 0 }

Write-Host "2/4 Empaquetando el payload (payload.zip)..."
$payload = Join-Path $installerDir "FrubberPOS.Installer\payload.zip"
if (Test-Path $payload) { Remove-Item $payload -Force }
Compress-Archive -Path (Join-Path $root "artifacts\publish\*") -DestinationPath $payload -CompressionLevel Optimal -Force

Write-Host "3/4 Compilando el asistente WPF (exe unico)..."
$outDir = Join-Path $installerDir "output"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
dotnet publish $project `
    -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -o (Join-Path $outDir "_temp")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (instalador) fallo con codigo $LASTEXITCODE." }

$finalExe = Join-Path $outDir "FrubberPOS-Setup-$Version.exe"
Copy-Item (Join-Path $outDir "_temp\FrubberPOS-Setup.exe") $finalExe -Force
Remove-Item (Join-Path $outDir "_temp") -Recurse -Force
if (Test-Path $payload) { Remove-Item $payload -Force }

Write-Host "4/4 Listo: $finalExe"
