[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PostgresZip,
    [Parameter(Mandatory)][string]$ServerSetup
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'installer\server\ServerCluster.ps1')

if ((Get-FrubberSubnet ([ipaddress]'192.168.23.14') 23) -ne '192.168.22.0/23') {
    throw 'Calculo de subred incorrecto.'
}

$PostgresZip = (Resolve-Path -LiteralPath $PostgresZip).Path
$ServerSetup = (Resolve-Path -LiteralPath $ServerSetup).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ('frubber-jho121-' + [guid]::NewGuid().ToString('N'))
$data = Join-Path $work 'data'
$private = Join-Path $work 'private'
$certificate = Join-Path $work 'Servidor-Frubber.pem'
$profile = Join-Path $private 'CajaPrincipal.cfg'
$bin = Join-Path $work 'pgsql\bin'
$pgctl = Join-Path $bin 'pg_ctl.exe'
$postgres = Join-Path $bin 'psql.exe'
$scratchLog = Join-Path $work 'startup.log'
$started = $false
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
try { $port = $listener.LocalEndpoint.Port } finally { $listener.Stop() }
if ($port -eq 5432) { throw 'La prueba requiere un puerto temporal.' }

try {
    New-Item -ItemType Directory -Path $data, $private | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($PostgresZip, $work)
    $password = ([guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N'))
    Initialize-FrubberCluster -PgBin $bin -Data $data -SecretDirectory $private -Setup $ServerSetup `
        -Address ([ipaddress]::Loopback) -Prefix 32 -Port $port -Password $password
    Copy-Item -LiteralPath (Join-Path $data 'server.crt') -Destination $certificate
    $hba = Get-Content -LiteralPath (Join-Path $data 'pg_hba.conf') -Raw
    if ($hba -match '\b(trust|md5)\b' -or (Test-Path -LiteralPath (Join-Path $private 'initdb-password'))) {
        throw 'La configuracion de autenticacion o el archivo temporal son incorrectos.'
    }

    Invoke-FrubberNative $pgctl @('-D', $data, '-l', $scratchLog, '-w', 'start')
    $started = $true
    Initialize-FrubberDatabase -Setup $ServerSetup -Port $port -Certificate $certificate -Profile $profile `
        -Password $password -Pin '6417'
    if (-not (Test-Path -LiteralPath $profile -PathType Leaf)) { throw 'No se creo el perfil de la caja.' }

    $env:PGPASSWORD = $password
    $env:PGSSLMODE = 'verify-full'
    $env:PGSSLROOTCERT = $certificate
    $query = @'
SELECT current_setting('server_version_num') || '|' ||
       (SELECT count(*) FROM public.schema_migration) || '|' ||
       (SELECT count(*) FROM public.terminal WHERE is_active) || '|' ||
       (SELECT count(*) FROM public.trial_state) || '|' ||
       (SELECT ssl FROM pg_stat_ssl WHERE pid = pg_backend_pid())::text || '|' ||
       (lower(chr(193)) = chr(225))::text;
'@
    $answer = Invoke-FrubberNative $postgres @('-X', '-h', '127.0.0.1', '-p', $port,
        '-U', 'postgres', '-d', 'frubber', '-v', 'ON_ERROR_STOP=1', '-Atc', $query)
    if ($answer -ne '180007|1|1|1|true|true') { throw "Resultado inesperado: $answer" }

    $env:PGSSLMODE = 'disable'
    $ErrorActionPreference = 'Continue'
    try {
        & $postgres -X -h 127.0.0.1 -p $port -U postgres -d frubber -Atc 'SELECT 1' 2>$null
        if ($LASTEXITCODE -eq 0) { throw 'Se acepto una conexion sin TLS.' }
    } finally { $ErrorActionPreference = 'Stop' }

    Copy-Item -LiteralPath (Join-Path $root 'installer\server\Respaldo-Servidor.ps1') -Destination $work
    Copy-Item -LiteralPath (Join-Path $root 'scripts\backup-frubberpos.ps1') -Destination $work
    Protect-FrubberBackupPassword -Password $password -Path (Join-Path $private 'backup.dpapi')
    Remove-Item Env:PGPASSWORD
    $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    Invoke-FrubberNative $windowsPowerShell @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
        '-File', (Join-Path $work 'Respaldo-Servidor.ps1'), '-Port', $port)
    $backupFolder = Join-Path $work 'backups'
    $dump = @(Get-ChildItem -LiteralPath $backupFolder -Filter 'FrubberPOS_backup_*.dump')
    $roles = @(Get-ChildItem -LiteralPath $backupFolder -Filter 'FrubberPOS_roles_*.sql')
    if ($dump.Count -ne 1 -or $roles.Count -ne 1 -or $roles[0].Length -eq 0) {
        throw 'Falta el respaldo de datos o de roles.'
    }
    Invoke-FrubberNative (Join-Path $bin 'pg_restore.exe') @('--list', $dump[0].FullName) | Out-Null
    Write-Host 'PASS: PostgreSQL 18.7, esquema, puesto, PIN, TLS verificado, rechazo sin TLS y respaldo diario.'
} finally {
    Remove-Item Env:PGPASSWORD, Env:PGSSLMODE, Env:PGSSLROOTCERT -ErrorAction SilentlyContinue
    if ($started) { Invoke-FrubberNative $pgctl @('-D', $data, '-m', 'fast', '-w', 'stop') }
    if (Test-Path -LiteralPath $work) {
        $resolved = (Resolve-Path -LiteralPath $work).Path
        $expectedParent = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path.TrimEnd('\')
        if ((Split-Path -Parent $resolved) -eq $expectedParent -and
            (Split-Path -Leaf $resolved) -match '^frubber-jho121-[0-9a-f]{32}$') {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
