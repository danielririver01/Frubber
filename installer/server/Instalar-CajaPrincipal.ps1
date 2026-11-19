[CmdletBinding()]
param([ipaddress]$DireccionLan)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ServerCluster.ps1')

function Read-FrubberSecret {
    param([string]$Prompt)
    $secure = Read-Host $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        $secure.Dispose()
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Abra Windows PowerShell como administrador con la cuenta que usara la caja principal.'
}
if (-not [Environment]::Is64BitProcess -or
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'Este paquete requiere Windows x64 y PowerShell de 64 bits.'
}

$service = 'FrubberPOSPostgreSQL'
$task = 'FrubberPOS-Servidor-Respaldo'
$rule = 'FrubberPOS-Servidor-PostgreSQL'
$port = 5432
$root = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'FrubberPOS-Servidor'

if ((Test-Path -LiteralPath $root) -or (Get-Service -Name $service -ErrorAction SilentlyContinue) -or
    (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue) -or
    (Get-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue) -or
    (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)) {
    throw 'Ya hay una instalacion de servidor, un recurso reservado o un servicio activo en el puerto 5432.'
}

if ($null -eq $DireccionLan) {
    Get-NetIPAddress -AddressFamily IPv4 -AddressState Preferred |
        Where-Object { $_.IPAddress -ne '127.0.0.1' } |
        Select-Object InterfaceAlias, IPAddress, PrefixLength | Format-Table | Out-Host
    $DireccionLan = [ipaddress](Read-Host 'IPv4 fija o reservada que usara esta caja en la red local')
}
$interface = @(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $DireccionLan.IPAddressToString `
    -AddressState Preferred -ErrorAction SilentlyContinue)
if ($interface.Count -ne 1 -or [ipaddress]::IsLoopback($DireccionLan) -or $interface[0].PrefixLength -eq 0) {
    throw 'Seleccione una IPv4 de la red local asignada a este Windows.'
}
$prefix = $interface[0].PrefixLength
$network = Get-FrubberSubnet $DireccionLan $prefix

$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'paquete.json') -Raw | ConvertFrom-Json
$archive = Join-Path $PSScriptRoot 'postgresql-18.7-1-windows-x64-binaries.zip'
$redistributable = Join-Path $PSScriptRoot 'vc_redist.x64.exe'
$setup = Join-Path $PSScriptRoot 'FrubberPOS.ServerSetup.exe'
$app = Join-Path $PSScriptRoot 'FrubberPOS-Setup.exe'
foreach ($path in @($archive, $redistributable, $setup, $app)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Falta un archivo del paquete: $path" }
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne
        'EAD0BBC27FB678FA24DCF86B738981323042ABD073C744103BF7F3AED23F9A33' -or
    (Get-FileHash -LiteralPath $redistributable -Algorithm SHA256).Hash -ne $manifest.visualCppSha256) {
    throw 'La verificacion SHA-256 de los componentes del servidor fallo.'
}

Write-Host 'El instalador prepara PostgreSQL, el primer puesto, una regla para la red local y el respaldo diario.'
$password = Read-FrubberSecret 'Contrasena de postgres nueva (16 a 128 caracteres ASCII visibles)'
if ($password -cnotmatch '^[!-~]{16,128}$' -or
    $password -cne (Read-FrubberSecret 'Repita la contrasena de postgres')) {
    throw 'La contrasena no cumple el formato o la confirmacion no coincide.'
}
$pin = Read-FrubberSecret 'PIN de cuatro digitos del administrador de Frubber'
if ($pin -cnotmatch '^[0-9]{4}$' -or $pin -cne (Read-FrubberSecret 'Repita el PIN')) {
    throw 'El PIN debe tener cuatro digitos y coincidir con su confirmacion.'
}

$data = Join-Path $root 'data'
$private = Join-Path $root 'private'
$bin = Join-Path $root 'pgsql\bin'
$certificate = Join-Path $root 'Servidor-Frubber.pem'
$profile = Join-Path $private 'CajaPrincipal.cfg'
$serviceReady = $false
$taskReady = $false
$ruleReady = $false
try {
    $prerequisite = Start-Process -FilePath $redistributable -ArgumentList '/install', '/quiet', '/norestart' `
        -Wait -PassThru -WindowStyle Hidden
    if ($prerequisite.ExitCode -notin 0, 1638, 3010) {
        throw "Fallo el requisito de Visual C++ (codigo $($prerequisite.ExitCode))."
    }
    if ($prerequisite.ExitCode -eq 3010) { Write-Warning 'Visual C++ solicito reiniciar Windows.' }

    New-Item -ItemType Directory -Path $root | Out-Null
    $icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'
    $administrators = '*S-1-5-32-544:(OI)(CI)F'
    $system = '*S-1-5-18:(OI)(CI)F'
    Invoke-FrubberNative $icacls @($root, '/inheritance:r', '/grant:r', $administrators, $system, '*S-1-5-19:(OI)(CI)RX')
    New-Item -ItemType Directory -Path $data, $private, (Join-Path $root 'backups') | Out-Null
    Invoke-FrubberNative $icacls @($data, '/grant:r', '*S-1-5-19:(OI)(CI)M')
    foreach ($restricted in @($private, (Join-Path $root 'backups'))) {
        Invoke-FrubberNative $icacls @($restricted, '/inheritance:r', '/grant:r', $administrators, $system)
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $root)
    Initialize-FrubberCluster -PgBin $bin -Data $data -SecretDirectory $private -Setup $setup `
        -Address $DireccionLan -Prefix $prefix -Port $port -Password $password
    Copy-Item -LiteralPath (Join-Path $data 'server.crt') -Destination $certificate

    $pgctl = Join-Path $bin 'pg_ctl.exe'
    Invoke-FrubberNative $pgctl @('register', '-N', $service, '-D', $data, '-S', 'auto', '-U', 'NT AUTHORITY\LocalService')
    $serviceReady = $true
    Start-Service -Name $service
    (Get-Service -Name $service).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))
    for ($tries = 0; $tries -lt 45; $tries++) {
        & (Join-Path $bin 'pg_isready.exe') -h 127.0.0.1 -p $port -t 1 -q
        if ($LASTEXITCODE -eq 0) { break }
        Start-Sleep -Milliseconds 500
    }
    if ($LASTEXITCODE -ne 0) { throw 'El servicio no empezo a aceptar conexiones.' }

    Initialize-FrubberDatabase -Setup $setup -Port $port -Certificate $certificate -Profile $profile `
        -Password $password -Pin $pin
    $backupLauncher = Join-Path $root 'Respaldo-Servidor.ps1'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Respaldo-Servidor.ps1') -Destination $backupLauncher
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'backup-frubberpos.ps1') -Destination $root
    Protect-FrubberBackupPassword -Password $password -Path (Join-Path $private 'backup.dpapi')
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $action = New-ScheduledTaskAction -Execute $powershell `
        -Argument ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}"' -f $backupLauncher)
    $trigger = New-ScheduledTaskTrigger -Daily -At ([datetime]::Today.AddHours(2))
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Settings $settings `
        -User 'SYSTEM' -RunLevel Highest | Out-Null
    $taskReady = $true

    New-NetFirewallRule -Name $rule -DisplayName 'Frubber POS | PostgreSQL red local' `
        -Direction Inbound -Action Allow -Protocol TCP -LocalAddress $DireccionLan.IPAddressToString `
        -LocalPort $port -RemoteAddress $network -Profile Private,Domain -Program (Join-Path $bin 'postgres.exe') |
        Out-Null
    $ruleReady = $true
}
catch {
    if ($ruleReady) { Remove-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue }
    if ($taskReady) { Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue }
    if ($serviceReady) {
        Stop-Service -Name $service -ErrorAction SilentlyContinue
        & $pgctl unregister -N $service
    }
    throw
}
finally {
    $password = $null
    $pin = $null
}

Write-Host "Servidor preparado. Certificado publico: $certificate"
Write-Host 'Complete el asistente de Frubber con esta misma cuenta de Windows.'
$wizard = Start-Process -FilePath $app `
    -ArgumentList ('--certificado-puesto "{0}" --perfil-puesto "{1}"' -f $certificate, $profile) -Wait -PassThru
if ($wizard.ExitCode -ne 0) { throw 'El servidor quedo preparado y falta completar el asistente de Frubber.' }
