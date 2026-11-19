$ErrorActionPreference = 'Stop'

function Invoke-FrubberNative {
    param([Parameter(Mandatory)][string]$File, [string[]]$Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Falló $([IO.Path]::GetFileName($File)) (código $LASTEXITCODE)." }
}

function Get-FrubberSubnet {
    param([ipaddress]$Address, [ValidateRange(1,32)][int]$Prefix)
    $bytes = $Address.GetAddressBytes()
    if ($bytes.Length -ne 4) { throw 'Se requiere una dirección IPv4.' }
    for ($i = 0; $i -lt 4; $i++) {
        $bits = [Math]::Min(8, [Math]::Max(0, $Prefix - 8 * $i))
        $bytes[$i] = $bytes[$i] -band (256 - (1 -shl (8 - $bits)))
    }
    return '{0}/{1}' -f ([ipaddress]::new($bytes)), $Prefix
}

function Initialize-FrubberCluster {
    param(
        [string]$PgBin, [string]$Data, [string]$SecretDirectory, [string]$Setup,
        [ipaddress]$Address, [ValidateRange(1,32)][int]$Prefix, [int]$Port,
        [string]$Password
    )
    $version = & (Join-Path $PgBin 'postgres.exe') --version
    if ($LASTEXITCODE -ne 0 -or $version -notmatch '\(PostgreSQL\) 18\.7$') {
        throw 'El paquete debe contener PostgreSQL 18.7.'
    }
    if (Test-Path -LiteralPath (Join-Path $Data 'PG_VERSION')) {
        throw 'Ya existe una base PostgreSQL en la carpeta de datos.'
    }
    $passwordFile = Join-Path $SecretDirectory 'initdb-password'
    try {
        $stream = [IO.File]::Open($passwordFile, 'CreateNew', 'Write', 'None')
        try {
            $writer = [IO.StreamWriter]::new($stream, [Text.Encoding]::ASCII)
            try { $writer.WriteLine($Password) } finally { $writer.Dispose() }
        } finally { $stream.Dispose() }
        Invoke-FrubberNative (Join-Path $PgBin 'initdb.exe') @(
            '-D', $Data, '-U', 'postgres', '--encoding=UTF8', '--locale-provider=builtin', '--builtin-locale=C.UTF-8',
            '--auth-host=scram-sha-256', '--auth-local=scram-sha-256', "--pwfile=$passwordFile"
        )
    } finally {
        if (Test-Path -LiteralPath $passwordFile -PathType Leaf) {
            Remove-Item -LiteralPath $passwordFile -Force
        }
    }

    Invoke-FrubberNative $Setup @('certificado', $Data, $Address.IPAddressToString)
    $listen = @('127.0.0.1', $Address.IPAddressToString) | Select-Object -Unique
    $network = Get-FrubberSubnet $Address $Prefix
    @"

# Frubber POS 2.0
listen_addresses = '$($listen -join ',')'
port = $Port
ssl = on
ssl_cert_file = 'server.crt'
ssl_key_file = 'server.key'
password_encryption = 'scram-sha-256'
logging_collector = on
log_directory = 'log'
"@ | Add-Content -LiteralPath (Join-Path $Data 'postgresql.conf') -Encoding Ascii

    @"
# Puestos del LAN y administración local por TLS.
hostssl all     postgres         127.0.0.1/32 scram-sha-256
hostssl frubber +frubber_puesto  127.0.0.1/32 scram-sha-256
hostssl frubber +frubber_puesto  $network scram-sha-256
"@ | Set-Content -LiteralPath (Join-Path $Data 'pg_hba.conf') -Encoding Ascii
}

function Initialize-FrubberDatabase {
    param([string]$Setup, [int]$Port, [string]$Certificate, [string]$Profile,
          [string]$Password, [string]$Pin)
    $Password, $Pin | & $Setup inicializar $Port $Certificate $Profile
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo preparar la base o la primera caja.' }
}

function Protect-FrubberBackupPassword {
    param([string]$Password, [string]$Path)
    Add-Type -AssemblyName System.Security
    $bytes = [Text.Encoding]::UTF8.GetBytes($Password)
    try {
        $encrypted = [Security.Cryptography.ProtectedData]::Protect(
            $bytes, $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
        [IO.File]::WriteAllBytes($Path, $encrypted)
    } finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}
