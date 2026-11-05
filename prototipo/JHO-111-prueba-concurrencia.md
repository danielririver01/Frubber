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

## Evidencia reportada por el usuario · Windows · 2026-11-05

El usuario informó la ejecución en PostgreSQL 18.6 con el rol de pruebas `frubber` y permiso `CREATEDB`, sin superusuario, en una copia separada del repositorio. La salida comunicada fue:

```text
PostgreSQL 18.6
OK: bloque 2 del prototipo original (100 tickets y rollback).
OK: 100 ventas consecutivas, tickets 1 a 100, cero huecos.
OK: venta fallida revertida; el siguiente ticket reutiliza el 101.
OK: 500 ventas concurrentes, 10 procesos y 10 conexiones, 250 por puesto.
OK: 500 números distintos y consecutivos (102–601); 0 duplicados, 0 huecos.
OK: 601 ventas, 601 líneas y contador final 601.
Base temporal eliminada.
Codigo de salida: 0
```

Según el usuario, no quedó ninguna base `frubber_jho111_*`. Esta ejecución fue reportada por el usuario, no observada directamente por el agente; prueba la misma línea base 2.0 y no el flujo de cobro de la aplicación.

## Evidencia reportada por el usuario · cobro actual · Windows

El usuario informó que ejecutó `TicketNumberIntegrationTests` en Windows, con PostgreSQL 18 local y el código en el commit `27f0fff`. Compilaron los cinco proyectos y pasaron **2 de 2 pruebas**, con código de salida 0 y duración de 5,0483 segundos:

```text
Correctas FrubberPOS.Tests.Sales.TicketNumberIntegrationTests.CheckoutFailureAfterNumberAndHeader_RollsBackAndReusesNumber [1 s]
Correctas FrubberPOS.Tests.Sales.TicketNumberIntegrationTests.ConcurrentCheckout_500Sales_ProducesConsecutiveNumbersAndCompleteSales [1 s]
Pruebas totales: 2
Correcto: 2
Codigo de salida: 0
```

La prueba concurrente usa diez trabajadores que realizan 50 ventas cada uno; no son 500 operaciones simultáneas. Según el usuario no quedaron bases temporales `frubberpos_scratch_*`. Informó dos avisos xUnit2013 preexistentes en `ProductsViewModelTests.cs`. Esta evidencia no fue observada directamente por el agente y corresponde al flujo actual con una caja y las migraciones embebidas, no al esquema 2.0 ni a dos puestos reales.

## Alcance y pendiente de integración

Esta evidencia demuestra el comportamiento de `ticket_counter` y `next_ticket()` **de la línea base 2.0**, insertando directamente las ventas sintéticas. No prueba el ejecutable de Windows, la operación de la aplicación ni la ruta de venta con credenciales restringidas. Se ejecutó únicamente el bloque 2 del banco histórico mediante psycopg, no el archivo completo con `psql`; además se comprobaron directamente 100 ventas y rollback contra la línea base.

El código de la aplicación usa las migraciones embebidas 001–004 y `SystemSequence`, no la línea base 2.0 y `ticket_counter`. Como ya se registró en JHO-113, son dos esquemas diferentes: este resultado no demuestra que la aplicación esté integrada con el esquema probado. El 2026-11-05 el dueño confirmó continuar con la línea base 2.0 (`database/postgresql/001_Baseline.sql`), conforme a ADR-029 y JHO-109. La elección está resuelta. Las pruebas del contador de JHO-111 no demuestran la integración de la aplicación con esa base: ese trabajo y la prueba del flujo real de cobro se siguen en JHO-136. El agente no ha ejecutado la suite .NET/WPF en Linux; el usuario informó las ejecuciones de las pruebas SQL y de dos pruebas .NET en Windows, descritas arriba.

### Registro transaccional con permisos de puesto · 2026-11-05

Se agregó `database/postgresql/003_RegistroVenta.sql`: una función que identifica el puesto por su conexión y registra número, cabecera, líneas e inventario en la misma transacción, sin conceder acceso directo a tablas sensibles. Se comprobó en PostgreSQL 18.6 real en Linux, en un clúster temporal aislado con dos roles de puesto y dos productos distintos. `prototipo/prueba-registro-venta-aislada.py` ejecuta la prueba; **solo debe usarse en un clúster desechable y dedicado**, no en un servidor compartido. La prueba crea y elimina una base llamada `frubber` y roles de clúster.

Salida de la ejecución más reciente, código 0:

```text
OK: permisos mínimos, caja de otro puesto y precio manipulado rechazados.
OK: solicitud repetida rechazada sin consumir un número; reintento idempotente pendiente.
OK: fallo tras insertar cabecera revierte la venta y reutiliza el número 3.
OK: 500 ventas concurrentes, dos puestos y diez conexiones: números 4 a 503, sin huecos ni duplicados.
OK: 503 ventas, 503 líneas, contador 503 e inventario correcto.
OK: dos solicitudes simultáneas con el mismo identificador crean una sola venta y ningún hueco.
Base y roles temporales eliminados.
```

Se verificó la limpieza de bases y roles y se repitió tres veces la prueba anterior de 500 operaciones. SHA-256 de 003: `1ce0693b0371758047aeee8230e4c2e23f8db7457454c86a0f608d311bda99e7`; SHA-256 del banco: `f2340e1f26711af553bff8207a1926b288a2e76873b2e6963b25488323a350b4`.

**Límites:** la app y el instalador aún no aplican ni llaman esta función; la prueba no abre la interfaz ni ejecuta dos instalaciones Windows. Una repetición de solicitud se rechaza: no se devuelve aún la venta previa (JHO-114). La función comprueba que el cajero esté activo, pero no vincula su identidad a un PIN autenticado en el servidor; sigue aplicando el riesgo residual documentado en AD-03. La licencia también debe comprobarla la aplicación. No se ha habilitado la distribución.

### Verificación del impedimento de integración

El 2026-11-05 el agente aplicó `001_Baseline.sql` y `002_Security.sql` en un **servidor PostgreSQL 18.6 aislado y desechable**. La consulta del contador del repositorio actual falló por tabla inexistente (`SystemSequence`). Un rol de prueba miembro de `frubber_puesto` no tenía permiso de insertar en `sale`, leer directamente `cash_session` ni actualizar `ticket_counter`, y `SELECT next_ticket()` falló por permisos. Esto es coherente con la protección de tablas críticas y demuestra que no basta con cambiar el nombre de la tabla: el flujo 2.0 también necesita operaciones autorizadas compatibles con la matriz de seguridad. No se debe conceder escritura directa ni utilizar un superusuario en los puestos para hacer pasar el cobro. Esta comprobación se realizó directamente en PostgreSQL, no ejecutando la aplicación.
