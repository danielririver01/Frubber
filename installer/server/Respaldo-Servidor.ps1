param([ValidateRange(1,65535)][int]$Port = 5432)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$bin = Join-Path $PSScriptRoot 'pgsql\bin'
$secret = Join-Path $PSScriptRoot 'private\backup.dpapi'
$certificate = Join-Path $PSScriptRoot 'Servidor-Frubber.pem'
$password = [Security.Cryptography.ProtectedData]::Unprotect(
    [IO.File]::ReadAllBytes($secret), $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
try {
    Remove-Item Env:PGHOSTADDR, Env:PGSERVICE, Env:PGSERVICEFILE -ErrorAction SilentlyContinue
    $env:PGPASSWORD = [Text.Encoding]::UTF8.GetString($password)
    $env:PGSSLMODE = 'verify-full'
    $env:PGSSLROOTCERT = $certificate
    & (Join-Path $PSScriptRoot 'backup-frubberpos.ps1') -WarningAction Stop -BackupDir (Join-Path $PSScriptRoot 'backups') `
        -HostName '127.0.0.1' -Port $Port -User 'postgres' -Database 'frubber' `
        -PgDumpPath (Join-Path $bin 'pg_dump.exe') -PgDumpAllPath (Join-Path $bin 'pg_dumpall.exe')
} finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    [Array]::Clear($password, 0, $password.Length)
}
