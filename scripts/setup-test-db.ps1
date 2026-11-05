# JHO-113: prepara el entorno local para la suite de pruebas contra
# PostgreSQL real. Idempotente: se puede ejecutar cuantas veces se quiera.
#
# Requisitos: PostgreSQL local corriendo y psql disponible. La contraseña
# del superusuario se toma de $env:PGPASSWORD (o se pide si no está).
#
# Credenciales SOLO de desarrollo local: rotar antes de producción.
param(
    [string]$PsqlPath = "C:\Program Files\PostgreSQL\18\bin\psql.exe",
    [string]$AdminUser = "postgres",
    [string]$Host = "localhost",
    [string]$TestRole = "frubber",
    [string]$TestPassword = "frubber",
    [string]$TestDatabase = "frubberpos_test"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PsqlPath)) {
    throw "No se encontró psql en '$PsqlPath'. Ajuste el parámetro -PsqlPath."
}

if (-not $env:PGPASSWORD) {
    $env:PGPASSWORD = Read-Host "Contraseña del superusuario '$AdminUser'"
}

function Invoke-AdminSql([string]$Sql) {
    & $PsqlPath -U $AdminUser -h $Host -d postgres -v ON_ERROR_STOP=1 -tAc $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "Falló la ejecución de SQL contra el servidor: $Sql"
    }
}

# 1) Rol de pruebas con permiso para crear bases desechables
#    (las pruebas del ejecutor de migraciones crean y borran scratch DBs).
$roleExists = Invoke-AdminSql "SELECT 1 FROM pg_roles WHERE rolname = '$TestRole';"
if ($roleExists.Trim() -ne "1") {
    Write-Host "Creando el rol '$TestRole'..."
    & $PsqlPath -U $AdminUser -h $Host -d postgres -v ON_ERROR_STOP=1 -c `
        "CREATE ROLE $TestRole LOGIN PASSWORD '$TestPassword';"
    if ($LASTEXITCODE -ne 0) { throw "No se pudo crear el rol '$TestRole'." }
}
else {
    & $PsqlPath -U $AdminUser -h $Host -d postgres -v ON_ERROR_STOP=1 -c `
        "ALTER ROLE $TestRole LOGIN CREATEDB PASSWORD '$TestPassword';"
    if ($LASTEXITCODE -ne 0) { throw "No se pudo ajustar el rol '$TestRole'." }
}

# 2) Base de pruebas (CREATE DATABASE no puede correr dentro de una
#    transacción, por eso va como comando aparte).
$dbExists = Invoke-AdminSql "SELECT 1 FROM pg_database WHERE datname = '$TestDatabase';"
if ($dbExists.Trim() -ne "1") {
    Write-Host "Creando la base '$TestDatabase'..."
    & $PsqlPath -U $AdminUser -h $Host -d postgres -v ON_ERROR_STOP=1 -c `
        "CREATE DATABASE $TestDatabase OWNER $TestRole;"
    if ($LASTEXITCODE -ne 0) { throw "No se pudo crear la base '$TestDatabase'." }
}
else {
    Write-Host "La base '$TestDatabase' ya existe."
}

# 3) Verificación con el propio rol de pruebas.
$env:PGPASSWORD = $TestPassword
& $PsqlPath -U $TestRole -h $Host -d $TestDatabase -v ON_ERROR_STOP=1 -tAc "SELECT 1;"
if ($LASTEXITCODE -ne 0) { throw "El rol '$TestRole' no pudo conectarse a '$TestDatabase'." }

Write-Host ""
Write-Host "Entorno de pruebas listo:" -ForegroundColor Green
Write-Host "  FRUBBERPOS_TEST_CONNECTION_STRING='Host=$Host;Port=5432;Database=$TestDatabase;Username=$TestRole;Password=$TestPassword;'"
