# JHO-111: lanzador Windows del banco aislado. No acepta una base de destino:
# el programa crea y elimina una base temporal propia para cada ejecución.
# La conexión administrativa se proporciona en FRUBBER_JHO111_ADMIN_DSN.
$ErrorActionPreference = 'Stop'
if (-not $env:FRUBBER_JHO111_ADMIN_DSN) {
    throw 'Defina FRUBBER_JHO111_ADMIN_DSN con la conexión administrativa de prueba.'
}
$script = Join-Path $PSScriptRoot 'estres-concurrencia-postgresql.py'
python $script
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
