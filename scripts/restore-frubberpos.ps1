#=====================================================================
# Frubber POS 2.0.0
# Script: scripts\restore-frubberpos.ps1
# M2-30: Restauracion de respaldo con pg_restore (cuidados 2026-10-22)
#
# Reglas de este script (por que tiene "velocidad de barrera"):
#   1. Si la base destino EXISTE exige -ConfirmReplace $true. Nunca
#      arrasa una base por error de tipeo (el historial es inmutable
#      y no admite "reconstruir de memoria").
#   2. NO usa --no-privileges: los GRANT de 002_Security son parte del
#      estado correcto; los roles deben existir ANTES del restore
#      (FrubberPOS_roles_*.sql del backup, o 002_Security.sql re-ejecutado).
#   3. Al terminar imprime conteo de ventas y de migraciones para el
#      cuadre de la evidencia (E-05).
#
# Uso (base espejo NUEVA):
#   .\scripts\restore-frubberpos.ps1 -BackupFile respaldo.dump -TargetDb frubber_espejo
# Uso (sobrescribir existente, explicito):
#   .\scripts\restore-frubberpos.ps1 ... -TargetDb frubber_restore -ConfirmReplace $true
#=====================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateScript({Test-Path $_})]
    [string]$BackupFile,
    [Parameter(Mandatory=$true)][string]$TargetDb,
    [string]$PgRestorePath = "pg_restore",
    [bool]$ConfirmReplace = $false,
    [string]$RolesFile = ""         # opcional: FrubberPOS_roles_*.sql
)

if (-not (Get-Command $PgRestorePath -ErrorAction SilentlyContinue)) { throw "pg_restore no esta en el PATH" }
if (-not (Get-Command "psql" -ErrorAction SilentlyContinue)) { throw "psql no esta en el PATH (se usa para el candado y la verificacion)" }

if (-not $env:PGUSER) { throw "PGUSER no definido" }
if (-not $env:PGDATABASE) { $env:PGDATABASE = $TargetDb }   # console verification uses target DB

# --- candado: no sobrescribir sin permiso explicito -------------------
$existsSql = "SELECT 1 FROM pg_database WHERE datname = '$TargetDb'"
$exists = (psql -t -A -c $existsSql 2>$null)
if ($exists -match "1") {
    if (-not $ConfirmReplace) {
        throw "La base '$TargetDb' YA EXISTE. Para reemplazarla pasa -ConfirmReplace `$true (esto destruira su contenido)."
    }
} else {
    psql -v ON_ERROR_STOP=1 -c "CREATE DATABASE `"$TargetDb`" OWNER $env:PGUSER"
    if ($LASTEXITCODE -ne 0) { throw "No se pudo crear la base destino '$TargetDb'" }
    Write-Host "Base destino creada: $TargetDb"
}

# --- resultados (si el motor es nuevo carecen de roles) ---------------
if ($RolesFile -and (Test-Path $RolesFile)) {
    Write-Host "Aplicando archivo de roles: $RolesFile"
    # tolerante: en un mismo servidor los roles preexisten; solo interesan los NUEVOS
    psql -f $RolesFile 2>&1 | Select-String -Pattern "ERROR|error:"
    Write-Host "Resultados de rol aplicados (los duplicados se ignoran con un aviso visible arriba)."
}

# --- restauracion ------------------------------------------------------
Write-Host "Restaurando $BackupFile -> $TargetDb ..."
& $PgRestorePath -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER --dbname $TargetDb --clean --if-exists --no-owner $BackupFile
if ($LASTEXITCODE -ne 0) { Write-Error "pg_restore fallo ($LASTEXITCODE)"; exit 1 }

# --- verificacion de integridad para la evidencia ---------------------
Write-Host "---- CUADRE POST-RESTAURACION ----"
psql -t -A -F " | " -c "SELECT 'sale_rows', count(*) FROM sale UNION ALL SELECT 'sale_line_rows', count(*) FROM sale_line UNION ALL SELECT 'cash_session_rows', count(*) FROM cash_session UNION ALL SELECT 'schema_migration_rows', count(*) FROM schema_migration"
if ($LASTEXITCODE -ne 0) { Write-Error "La verificacion de integridad fallo: revisar logs."; exit 1 }
Write-Host "Restauracion verificada."
