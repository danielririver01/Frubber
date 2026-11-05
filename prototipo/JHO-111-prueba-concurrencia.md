# JHO-111 · Prueba aislada de numeración

## Ejecución reproducible

Requiere Python 3, `psycopg` 3 con soporte binario y un **servidor PostgreSQL de pruebas aislado**, nunca uno de producción. El usuario indicado debe poder crear y eliminar bases y ejecutar la línea base. El banco crea una base nueva con nombre aleatorio `frubber_jho111_<uuid>`, aplica `database/postgresql/001_Baseline.sql`, inserta datos sintéticos, y elimina únicamente esa base al terminar, incluso si una comprobación falla. No desactiva disparadores ni borra filas de otras bases. Si se interrumpe el proceso de forma abrupta, revisar si quedó alguna base temporal antes de eliminarla manualmente.

Ejemplo en PowerShell (credenciales de prueba mediante `PGPASSFILE` u otro mecanismo de libpq, no en archivos del proyecto):

```powershell
python -m pip install "psycopg[binary]>=3.2,<4"
$env:FRUBBER_JHO111_ADMIN_DSN = 'host=127.0.0.1 port=5432 dbname=postgres user=usuario_de_prueba'
.\prototipo\estres-concurrencia-postgresql.ps1
if ($LASTEXITCODE -ne 0) { throw 'Falló la prueba JHO-111' }
```

También se puede ejecutar `python prototipo/estres-concurrencia-postgresql.py` en Windows o Linux. La conexión suministrada es solo administrativa; la base probada siempre es la creada por el banco. Evitar escribir contraseñas en la consola, el historial, la evidencia o el repositorio. La prueba requiere permiso `CREATEDB`; para limpiar conexiones usa `DROP DATABASE ... WITH (FORCE)`.

El ensayo ejecuta también el bloque 2 del banco original `verificacion-postgresql.sql` dentro de un esquema aislado, y comprueba su resultado `OK` (sin ejecutar los demás bloques ni los metacomandos de `psql`). Luego crea dos puestos con caja abierta en la línea base real. Confirma 100 ventas individuales, provoca una violación de restricción **después de asignar el número y guardar la cabecera** y verifica el rollback y la reutilización del número. Por último sincroniza diez procesos independientes, cada uno con su propia conexión PostgreSQL, que ejecutan 50 transacciones de venta cada uno. Verifica números devueltos, ventas y líneas confirmadas, contador final, conexiones distintas y distribución entre puestos. Cada venta comprende número, cabecera y línea en una sola transacción. Si falla una comprobación o un proceso, el programa termina con código distinto de cero.

## Evidencia de ejecución · 2026-11-05 (UTC)

- Motor: PostgreSQL **18.6**, proceso real local en Linux; datos y base temporales.
- Línea base `001_Baseline.sql`, SHA-256: `a3686289867d0e3b679429d391858ef4c0fd2d4a8c797196d91387901b097c53`.
- Semilla SHA-256: `274a79e349276a1e82bbc6897fd727944d7c5d639c4e43fb30df3304687cdbd0`.
- Banco Python SHA-256: `98dba4ecce419cbc60c725d82d60b6ed39149d177e1406538f002543367999f6`.
- Comando: `python prototipo/estres-concurrencia-postgresql.py` con `FRUBBER_JHO111_ADMIN_DSN` apuntando al servidor local de prueba.
- Resultado (salida real, código 0):

```text
PostgreSQL 18.6
OK: bloque 2 del prototipo original (100 tickets y rollback).
OK: 100 ventas consecutivas, tickets 1 a 100, cero huecos.
OK: venta fallida revertida; el siguiente ticket reutiliza el 101.
OK: 500 ventas concurrentes, 10 procesos y 10 conexiones, 250 por puesto.
OK: 500 números distintos y consecutivos (102–601); 0 duplicados, 0 huecos.
OK: 601 ventas, 601 líneas y contador final 601.
Base temporal eliminada.
```

La semilla también se probó contra una base llamada `postgres`: rechazó la ejecución antes de modificarla. La consulta posterior no encontró bases temporales residuales.

## Alcance y pendiente de integración

Esta evidencia demuestra el comportamiento de `ticket_counter` y `next_ticket()` **de la línea base 2.0**, insertando directamente las ventas sintéticas. No prueba el ejecutable de Windows, la operación de la aplicación ni la ruta de venta con credenciales restringidas. Se ejecutó únicamente el bloque 2 del banco histórico mediante psycopg, no el archivo completo con `psql`; además se comprobaron directamente 100 ventas y rollback contra la línea base.

El código de la aplicación usa las migraciones embebidas 001–004 y `SystemSequence`, no la línea base 2.0 y `ticket_counter`. Como ya se registró en JHO-113, son dos esquemas diferentes: este resultado no demuestra que la aplicación esté integrada con el esquema probado. Antes de dar por cerrada la integración de JHO-111, hay que resolver esa discrepancia documental y probar el flujo real de cobro; no se ha escogido un esquema por cuenta propia. La suite .NET/WPF y una ejecución en Windows no se verificaron en este entorno Linux sin SDK .NET.
