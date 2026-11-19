[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PostgresZip,
    [Parameter(Mandatory)][string]$VisualCppExe,
    [string]$Version = '2.0.0-lab.4'
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[a-zA-Z0-9.-]+)?$') { throw 'Version invalida.' }
$root = Split-Path $PSScriptRoot -Parent
$engine = (Resolve-Path -LiteralPath $PostgresZip).Path
$visualCpp = (Resolve-Path -LiteralPath $VisualCppExe).Path
$engineHash = (Get-FileHash -LiteralPath $engine -Algorithm SHA256).Hash
$visualCppHash = (Get-FileHash -LiteralPath $visualCpp -Algorithm SHA256).Hash
if ($engineHash -ne 'EAD0BBC27FB678FA24DCF86B738981323042ABD073C744103BF7F3AED23F9A33' -or
    $visualCppHash -ne '9C02C9ED120F671B900E6B9471D890E3B2414117A7E53F4AE2E855466FA2B7C9') {
    throw 'Los binarios no corresponden a los insumos publicados para Caja principal.'
}

$artifacts = Join-Path $root 'artifacts'
$staging = Join-Path $artifacts ('server-package-' + [guid]::NewGuid().ToString('N'))
$helper = Join-Path $staging 'helper'
$bundle = Join-Path $staging 'package'
$package = Join-Path $PSScriptRoot "output\FrubberPOS-CajaPrincipal-$Version.zip"
if (Test-Path -LiteralPath $package) { throw 'Ya existe un paquete con esa version.' }

try {
    New-Item -ItemType Directory -Path $helper, $bundle | Out-Null
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -Version $Version
    dotnet publish (Join-Path $root 'tools\FrubberPOS.ServerSetup\FrubberPOS.ServerSetup.csproj') `
        -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -o $helper
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el preparador de servidor.' }
    Copy-Item -LiteralPath (Join-Path $helper 'FrubberPOS.ServerSetup.exe') -Destination $bundle
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "output\FrubberPOS-Setup-$Version.exe") `
        -Destination (Join-Path $bundle 'FrubberPOS-Setup.exe')
    Copy-Item -LiteralPath $engine -Destination (Join-Path $bundle 'postgresql-18.7-1-windows-x64-binaries.zip')
    Copy-Item -LiteralPath $visualCpp -Destination (Join-Path $bundle 'vc_redist.x64.exe')
    Copy-Item -LiteralPath (Join-Path $root 'scripts\backup-frubberpos.ps1') -Destination $bundle
    foreach ($file in 'Instalar-CajaPrincipal.ps1', 'ServerCluster.ps1', 'Respaldo-Servidor.ps1', 'README.md') {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "server\$file") -Destination $bundle
    }
    @{
        version = $Version
        postgres = '18.7'
        postgresSha256 = $engineHash
        visualCppSha256 = $visualCppHash
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle 'paquete.json') -Encoding Ascii

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temporary = Join-Path $staging 'caja-principal.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($bundle, $temporary, [IO.Compression.CompressionLevel]::Fastest, $false)
    Move-Item -LiteralPath $temporary -Destination $package
    Get-FileHash -LiteralPath $package -Algorithm SHA256
} finally {
    if (Test-Path -LiteralPath $staging) {
        $resolved = (Resolve-Path -LiteralPath $staging).Path
        $expectedParent = (Resolve-Path -LiteralPath $artifacts).Path
        if ((Split-Path -Parent $resolved) -eq $expectedParent -and
            (Split-Path -Leaf $resolved) -match '^server-package-[0-9a-f]{32}$') {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
