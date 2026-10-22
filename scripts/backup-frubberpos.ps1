#=====================================================================
# Frubber POS 2.0.0
# Script: scripts\backup-frubberpos.ps1
# M2-30: Respaldo automatico con pg_dump (cuidados del 2026-10-22)
#
# Genera respaldo de la base frubber en formato personalizado (-Fc) y,
# si RolesOnly también activa (por defecto), un archivo de ROLES del
# cluster con pg_dumpall --roles-only: sin esos roles, una restauracion
# nueva del motor no puede crear usuarios de puesto (002_Security).
#
# Requisitos (uno de los dos):
#   - PGPASSWORD en el entorno, o pgpass.conf del usuario que lanza la tarea.
#   - PGUSER debe ser un superusuario del motor (postgres en el local).
#
# Uso:
#   .\scripts\backup-frubberpos.ps1 -BackupDir "C:\Backups\FrubberPOS"
#=====================================================================

[CmdletBinding()]
param(
    [string]$BackupDir = "C:\Backups\FrubberPOS",
    [int]$RetentionDays = 30,
    [string]$PgDumpPath = "pg_dump",
    [string]$PgDumpAllPath = "pg_dumpall",
    # PGHOST default localhost; en tarea SYSTEM el DSN del servidor local
    $HostName = $null,
    $Port = $null,
    $User = $null,
    $Database = $null
)

# --- validacion temprana: nada de args con valores vacios ------------
if (-not $HostName) { $HostName = if ($env:PGHOST) { $env:PGHOST } else { "localhost" } }
if (-not $Port)     { $Port     = if ($env:PGPORT) { $env:PGPORT } else { "5432" } }
if (-not $User)     { $User     = if ($env:PGUSER) { $env:PGUSER } else { throw "PGUSER no definido: sin usuario no hay respaldo autenticado" } }
if (-not $Database) { $Database = if ($env:PGDATABASE) { $env:PGDATABASE } else { "frubber" } }

foreach ($tool in @($PgDumpPath, $PgDumpAllPath)) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "Herramienta no encontrada en el PATH: $tool (pasa la ruta completa con -PgDumpPath)"
    }
}

New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null

# --- archivo de respaldo principal -----------------------------------
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupFile = Join-Path $BackupDir ("FrubberPOS_backup_{0}.dump" -f $timestamp)

Write-Host "Iniciando respaldo de la base $Database ..."
& $PgDumpPath -h $HostName -p $Port -U $User -Fc -f $backupFile $Database
if ($LASTEXITCODE -ne 0) {
    Write-Error "pg_dump fallo con codigo $LASTEXITCODE (respaldo incompleto: se intentara reproducir con la tarea siguiente)"
    exit 1
}
Write-Host "Respaldo listo: $backupFile"

# --- archivo de roles del cluster (vital para restaurar en motor nuevo)
$rolesFile = Join-Path $BackupDir ("FrubberPOS_roles_{0}.sql" -f $timestamp)
& $PgDumpAllPath -h $HostName -p $Port -U $User --roles-only -f $rolesFile
if ($LASTEXITCODE -ne 0) {
    Write-Warning "pg_dumpall --roles-only fallo ($LASTEXITCODE). El .dump existe pero sin roles el restore en un motor nuevo no tendra puestos/permisos."
} else {
    Write-Host "Roles listos: $rolesFile"
}

# --- retencion --------------------------------------------------------
if ($RetentionDays -gt 0) {
    $cutoffDate = (Get-Date).AddDays(-$RetentionDays)
    Get-ChildItem -Path $BackupDir -File -Include "FrubberPOS_backup_*.dump","FrubberPOS_roles_*.sql" -Recurse |
        Where-Object { $_.CreationTime -lt $cutoffDate } |
        Remove-Item -Force
    Write-Host "Retencion: archivos de mas de $RetentionDays dias eliminados."
}
