# =============================================================================
#  Frubber POS 2.0.0
#  Archivo: prototipo/estres-concurrencia-postgresql.ps1
#  JHO-111 (M2-29) · Prueba de concurrencia real del contador de tickets.
#
#  Objetivo: 500 ventas concurrentes contra la base real "frubber"
#  (PostgreSQL 18.6, esquema 001 + security 002), lanzando lotes de procesos
#  psql en paralelo. Cada venta es su propia transaccion (archivo SQL con un
#  solo INSERT ... ; INSERT ... -> simple query multi-statement, transaccion
#  atomica) y consume el numero con next_ticket() (UPDATE ... RETURNING con
#  bloqueo de fila en la PK de ticket_counter).
#
#  Criterios de JHO-111:
#    (1) 100 tickets consecutivos, 0 saltos
#    (2) venta fallida no deja hueco            -> test de rollback (manual)
#    (3) prueba de concurrencia con 2+ procesos -> este estrés
#    (4) 500 ventas concurrentes documentadas -> este estrés
#
#  Uso:
#    1. psql -U postgres -d frubber -v ON_ERROR_STOP=1 -f prototipo/seed-concurrencia-postgresql.sql
#    2. pwsh -File prototipo/estres-concurrencia-postgresql.ps1
# =============================================================================

$ErrorActionPreference = 'Stop'

$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$baseArgs = @('-U', 'postgres', '-d', 'frubber', '-v', 'ON_ERROR_STOP=1')
$guid = [guid]::NewGuid().ToString()

function New-SaleSqlFile {
    param([string]$Guid)
    $sql = @"
INSERT INTO sale (sale_number, cash_session_id, cashier_user_id, cashier_name_snapshot, subtotal_cop, discount_cop, total_cop, payment_method, received_cop, change_cop, client_request_id) VALUES (next_ticket(), (SELECT cs.cash_session_id FROM cash_session cs WHERE cs.closed_at IS NULL ORDER BY cs.cash_session_id LIMIT 1), (SELECT u.user_id FROM app_user u WHERE u.display_name = 'Verif Cajero' AND u.is_active = true), 'Verif Cajero', 1500, 0, 1500, 'CASH', 1500, 0, '$Guid'::uuid);
INSERT INTO sale_line (sale_id, line_number, product_id, plu_snapshot, product_name_snapshot, unit_snapshot, unit_price_cop_snapshot, quantity_milli, line_total_cop) SELECT s.sale_id, 1, p.product_id, 'VERIF29', 'Producto verif M2-29', 'KG', p.price_cop, 1000, (p.price_cop * 1000 + 500) / 1000 FROM sale s JOIN product p ON p.plu = 'VERIF29' WHERE s.client_request_id = '$Guid'::uuid;
"@
    $path = "$env:TEMP\opencode\est_sale_$Guid.sql"
    Set-Content -Path $path -Value $sql -NoNewline
    return $path
}

$totalSales = 500
$waves = 10
$perWave = 50
$fallos = @()
$done = 0

"=== Estrés M2-29: 500 ventas concurrentes (PostgreSQL 18.6, base real frubber) ==="
"=== Onda: $perWave ventas concurrentes por oleada, $waves oleadas ==="

for ($w = 0; $w -lt $waves; $w++) {
    $procs = @()
    for ($i = 0; $i -lt $perWave; $i++) {
        $guid = [guid]::NewGuid().ToString()
        $sqlPath = New-SaleSqlFile -Guid $guid
        $p = Start-Process -FilePath $psql -ArgumentList ($baseArgs + @('-f', $sqlPath)) -PassThru -RedirectStandardOutput "$env:TEMP\opencode\est_w${w}_$i.out" -RedirectStandardError "$env:TEMP\opencode\est_w${w}_$i.err"
        $procs += $p
    }
    foreach ($p in $procs) { $p.WaitForExit() }
    Write-Host "Onda $w completada."
}

Write-Host "ESTRES_FINALIZADO"
