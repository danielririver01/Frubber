#=====================================================================
# Frubber POS
# Script: scripts\preparar-bd-pruebas.ps1
# JHO-113: prepara el PostgreSQL local que exige la suite de pruebas.
#
# QUE HACE (idempotente, se puede relanzar sin miedo):
#   1. Crea el rol "frubber" (login, password "frubber") si no existe.
#   2. Le da CREATEDB: las pruebas del ejecutor de migraciones crean una
#      base desechable por prueba (PostgresTestDb.CreateScratchAsync).
#   3. Crea la base "frubberpos_test" si no existe. Las migraciones 001-004
#      las aplica la propia suite; el script no toca el esquema.
#
# POR QUE: sin este paso, una maquina nueva (o un CI limpio) falla con
# "database frubberpos_test does not exist" / "permission denied to create
# database" y parece un fallo de codigo cuando es del entorno.
#
# USO:
#   .\scripts\preparar-bd-pruebas.ps1
#   .\scripts\preparar-bd-pruebas.ps1 -PgBin "C:\Program Files\PostgreSQL\16\bin"
#   .\scripts\preparar-bd-pruebas.ps1 -ConnectionString "Host=otro;Port=5432;..."
#
# Si el PostgreSQL no es el local por defecto, exporta antes:
#   $env:FRUBBERPOS_TEST_CONNECTION_STRING = "..."
# y la suite usara esa cadena en lugar de la de desarrollo.
#=====================================================================

[CmdletBinding()]
param(
    [string]$PgBin = "C:\Program Files\PostgreSQL\16\bin",
    [string]$AdminUser = "postgres",
    [string]$DbName = "frubberpos_test",
    [string]$TestUser = "frubber",
    [string]$TestPassword = "frubber",
    [string]$HostName = "localhost",
    [int]$Port = 5432
)

$ErrorActionPreference = "Stop"

$psql = Join-Path $PgBin "psql.exe"
if (-not (Test-Path $psql)) {
    throw "No se encontro psql.exe en '$PgBin'. Indica la carpeta bin de PostgreSQL con -PgBin."
}

function Invoke-Psql {
    param([string]$Database, [string]$Sql)

    $out = & $psql --host $HostName --port $Port --username $AdminUser `
                   --dbname $Database --no-psqlrc --tuples-only --no-align `
                   --set ON_ERROR_STOP=1 --command $Sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "psql fallo contra '$Database': $out"
    }
    return $out
}

Write-Host "== Frubber POS: preparacion de la base de pruebas ==" -ForegroundColor Cyan
Write-Host "Servidor: ${HostName}:$Port  |  base: $DbName  |  rol: $TestUser"

# 1. Rol de pruebas con CREATEDB (las pruebas crean bases desechables).
$roleExists = (Invoke-Psql -Database "postgres" -Sql "SELECT 1 FROM pg_roles WHERE rolname = '$TestUser';").Trim()
if ($roleExists -eq "1") {
    Invoke-Psql -Database "postgres" `
        -Sql "ALTER ROLE $TestUser WITH LOGIN CREATEDB PASSWORD '$TestPassword';" | Out-Null
    Write-Host "  [ok] rol '$TestUser' ya existia (CREATEDB refrescado)" -ForegroundColor Green
}
else {
    Invoke-Psql -Database "postgres" `
        -Sql "CREATE ROLE $TestUser WITH LOGIN CREATEDB PASSWORD '$TestPassword';" | Out-Null
    Write-Host "  [ok] rol '$TestUser' creado con CREATEDB" -ForegroundColor Green
}

# 2. Base de pruebas (el esquema lo crean las migraciones de la suite).
$dbExists = (Invoke-Psql -Database "postgres" -Sql "SELECT 1 FROM pg_database WHERE datname = '$DbName';").Trim()
if ($dbExists -eq "1") {
    Write-Host "  [ok] base '$DbName' ya existia (la suite la migra y la limpia)" -ForegroundColor Green
}
else {
    Invoke-Psql -Database "postgres" -Sql "CREATE DATABASE $DbName OWNER $TestUser;" | Out-Null
    Write-Host "  [ok] base '$DbName' creada" -ForegroundColor Green
}

# 3. Recordatorio de la cadena que usan las pruebas.
$connectionString = "Host=$HostName;Port=$Port;Database=$DbName;Username=$TestUser;Password=$TestPassword;"
Write-Host ""
Write-Host "Cadena de pruebas (la usa PostgresTestDb por defecto):" -ForegroundColor Yellow
Write-Host "  $connectionString"
Write-Host ""
Write-Host "Si tu servidor no es ese, define la variable antes de correr las pruebas:" -ForegroundColor Yellow
Write-Host "  `$env:FRUBBERPOS_TEST_CONNECTION_STRING = `"$connectionString`""
Write-Host ""
Write-Host "Ejecuta la suite con:" -ForegroundColor Yellow
Write-Host "  dotnet test tests\FrubberPOS.Tests\FrubberPOS.Tests.csproj -c Release"
