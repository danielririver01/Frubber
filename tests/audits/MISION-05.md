# Misión 05 — PostgreSQL 42501 (JHO-140)

Fecha: 2026-11-18. Resultado: **no reproducido; posible fallo intermitente**.
Suite completa: **770 correctas, 0 fallidas, 5 omitidas, 775 totales**.

## Evidencia y causa identificada

1. Se ejecutó primero, sin cambios, únicamente
   `Postgres20BaselineInstallerTests.FailureAfterBaseline_RollsBackSchemaAndLedger_ThenCanRetry`:
   **1 correcta, 0 fallidas**, Release. El rollback, el reintento y la eliminación
   de su base temporal funcionaron.
2. Se inspeccionó `tests/FrubberPOS.Tests/TestInfrastructure/PostgresTestDb.cs`.
   `CreateScratchAsync` genera un nombre `frubberpos_scratch_` con GUID y crea la base
   usando el rol configurado de pruebas. `ScratchDatabase.DisposeAsync` llama a
   `NpgsqlConnection.ClearAllPools()`, conecta a `postgres` y ejecuta
   `DROP DATABASE IF EXISTS "<nombre generado>" WITH (FORCE)`.
3. Las conexiones de la prueba y del instalador se usan con disposición explícita.
   No se demostró una conexión sin liberar. Npgsql usa pooling: disponer una conexión
   normalmente la devuelve al pool, por lo que no equivale por sí solo a desconectar
   físicamente del servidor. El helper ya vacía los pools antes de eliminar la base.
4. Se consultó PostgreSQL local **18.6** usando el rol de pruebas `frubber`:
   `SUPERUSER=false`, `CREATEDB=true`, `CREATEROLE=false`, pertenencia a
   `pg_signal_backend=false`. No se alteró ningún permiso.
5. El registro histórico del servidor confirma el fallo de Misión 04 a las
   **11:50:35, America/Bogota**. Base:
   `frubberpos_scratch_2a7edb6d72b3447985193452d683a15c`.
   El detalle dice que solo el rol autorizado sobre el proceso o un miembro de
   `pg_signal_backend` puede terminarlo. El fallo ocurrió en DROP DATABASE WITH (FORCE),
   no en una aserción del rollback/reintento.

**Causa inmediata comprobada:** PostgreSQL rechazó por permisos el intento de terminar
un proceso asociado a esa base temporal durante el borrado forzado.
Ser dueño de la base no autoriza a terminar cualquier conexión.

**Causa raíz no identificada:** el registro no incluye PID, rol propietario ni tipo
del proceso bloqueante. Las consultas actuales no encontraron conexiones a las bases
scratch residuales; tampoco quedaron conexiones al finalizar la nueva suite.
No se puede afirmar que fuese un cliente externo, un worker, una fuga del pool o
interferencia entre pruebas. Atribuirle un rol concreto sería inventar evidencia.
La suite ya deshabilita el paralelismo entre pruebas; eso no excluye otros procesos
externos al testhost, pero tampoco demuestra que los hubiera en aquel instante.

Consultas de diagnóstico limitadas a `pg_roles`, `pg_database` y `pg_stat_activity`,
filtrando bases `frubberpos_scratch_*` / `frubberpos_test`. No se consultaron datos
comerciales ni se terminaron sesiones.

## Corrección mínima y segura

**No se implementa corrección:** el caso aislado y la suite completa pasaron sin
cambios. No hay evidencia suficiente para modificar el helper, agregar reintentos,
desactivar pooling, ocultar el 42501 o ampliar privilegios.

Si reaparece, el siguiente paso es capturar en ese momento, antes de perder el
proceso bloqueante, PID, usename, backend_type, application_name y estado,
filtrados por el nombre exacto de la base temporal recién creada. Solo con esa
evidencia procede decidir una corrección dirigida. No se recomienda conceder
SUPERUSER ni pg_signal_backend como solución.

Antes y después había las mismas dos bases residuales, propiedad de `frubber`:

- `frubberpos_scratch_2a7edb6d72b3447985193452d683a15c` (fallo de Misión 04).
- `frubberpos_scratch_3c26e2379ffb443292b4b9abd34fd530` (origen no atribuido).

No tenían conexiones en las consultas y se conservaron como evidencia; no se
borraron recursos de ejecuciones anteriores. La Misión 05 no dejó bases nuevas.

## Verificación

Comandos ejecutados con la conexión configurada explícitamente a `frubberpos_test`
en localhost:5432 y rol `frubber`:

```powershell
dotnet test tests/FrubberPOS.Tests/FrubberPOS.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~FailureAfterBaseline_RollsBackSchemaAndLedger_ThenCanRetry' --logger 'trx;LogFileName=mision-05-isolated-before.trx' --results-directory artifacts/mision-05
dotnet test FrubberPOS.sln -c Release --no-restore --logger 'trx;LogFileName=mision-05-full.trx' --results-directory artifacts/mision-05
```

| Ejecución | Correctas | Fallidas | Omitidas |
|---|---:|---:|---:|
| Prueba aislada, sin cambios | 1 | 0 | 0 |
| Suite completa, sin cambios | 770 | 0 | 5 |

Los 14 casos de cobro de Misión 04 permanecen sin modificar y pasan en la suite.
Se verificaron los hashes de 242 archivos de producción: **0 cambios** respecto
de la línea base de Misión 04. No se ejecutó Stryker.NET.

### Cinco pruebas de infraestructura 2.0 omitidas

| Prueba | Requisito que no se habilitó |
|---|---|
| Postgres20BaselineInstallerTests.CommitConnectionCut_ReturnsUnknownAndDirectRetryFindsInstalledBaseline | `FRUBBER_JHO136_ISOLATED=1`, solo en clúster desechable |
| Postgres20WorkstationRegistrationTests.EnforcesSignedSeatsAndSerializesConcurrentRegistrations | Clúster TLS desechable, indicador de aislamiento y `FRUBBER_JHO116_ADMIN` |
| WorkstationCertificateInstallerTests.InstallsCertificateAndProtectedProfileAndPreservesPreviousProfileOnFailure | Clúster TLS desechable, indicador de aislamiento y `FRUBBER_JHO129_ADMIN` |
| Postgres20WorkstationTests.RecoversExperimentalSaleAfterLostResponseAndNewWorkstationModel | Clúster TLS desechable, `FRUBBER_JHO136_WORKSTATION_ADMIN` e indicadores del escenario de recuperación |
| Postgres20WorkstationTests.TwoWorkstations_OpenSellAndCloseIndependently | Clúster TLS desechable, `FRUBBER_JHO136_WORKSTATION_ADMIN` e indicadores del escenario de dos puestos |

No se cambiaron sus condiciones ni se activaron pruebas destructivas 2.0 contra
el servidor local de pruebas generales para reducir artificialmente las omisiones.

## Archivos y artefactos

Único archivo añadido por Misión 05: `tests/audits/MISION-05.md`.
Sin cambios de infraestructura, producción, pruebas de cobro, descuentos ni permisos.
Los cambios locales previos de AGENTS.md y Misión 04 se conservaron.

Evidencia local bajo `artifacts/mision-05/`, ignorada por Git:

- `mision-05-isolated-before.trx`.
- `mision-05-full.trx`.
- `postgresql-42501-historical.txt` (extracto del error, detalle y DROP temporal).
- `postgresql-after.csv` (permisos, bases residuales y ausencia de conexiones).

## Fuentes oficiales

- [PostgreSQL 18: DROP DATABASE](https://www.postgresql.org/docs/18/sql-dropdatabase.html): FORCE solo termina procesos que el ejecutor tiene permiso de terminar.
- [Npgsql: conexiones y pooling](https://www.npgsql.org/doc/basic-usage.html#pooling): disposición lógica frente a conexión física.

Se recupera una ejecución completa en verde, con las cinco omisiones documentadas.
Esto no demuestra que la causa intermitente haya sido eliminada; no se presenta como
un defecto corregido. La validación global pendiente de Misión 04 queda satisfecha
por esta ejecución.
