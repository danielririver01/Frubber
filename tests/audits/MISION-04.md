# Misión 04 — Validaciones de cobro (JHO-139)

Fecha: 2026-11-18. Rama existente: `auditoria/mision-02-pruebas-criticas`.

## Alcance y fundamento

Solo se agregaron pruebas de `FinalizeSaleUseCase` en `Sales/FinalizeSaleValidationTests.cs`.
No se cambió producción, configuración versionada, descuentos, A2, A4 ni otros módulos.

Fuentes consultadas:

- Documento de Reglas de Negocio: RN-VEN-010/011/015/016 (páginas 8–9).
- SRS: RF-VEN-012/014/019/021/026/029/032 (páginas 8–10).
- Código y pruebas existentes; los siete informes originales de Misión 03 se conservaron.

Las reglas exigen efectivo recibido igual o superior al total, exactamente efectivo o
transferencia, ventas con productos, identidad del usuario y numeración persistente.
Los escenarios de catálogo verifican las defensas existentes ante borradores obsoletos;
no crean reglas comerciales nuevas.

## Pruebas agregadas: 14 casos

| Escenario | Casos | Resultado esperado |
|---|---:|---|
| Efectivo insuficiente: 0 y 4.199 para total 4.200 | 2 | Rechazo de validación; posterior pago exacto aceptado sin salto de número |
| Efectivo ausente o negativo: null y -1 | 2 | Rechazo indicando que el efectivo recibido es obligatorio |
| Venta vacía: lista vacía y null | 2 | Rechazo de validación |
| Pago no admitido: CARD, vacío y null | 3 | Rechazo de validación |
| Sesión cerrada por el usuario | 1 | Rechazo de autorización |
| Producto eliminado, unidad, PLU o nombre modificados después del borrador | 4 | NotFound/Conflict, sin cobrar un borrador obsoleto |

Cada caso prepara una caja con base de 50.000 y una venta real anterior de 4.200.
Antes y después del rechazo compara, desde conexiones independientes, todas las filas
y columnas de Sale, SaleLine, SaleVoid, Product, CashSession, V_OpenCashTotals y
SystemSequence. Esto protege también información ya existente, no solo tablas vacías.
En los cambios del catálogo, la comparación empieza después de la edición legítima.

Los dos casos de insuficiencia comprueban además el pago exacto: venta número 2,
recibido 4.200, cambio 0, stock descontado y efectivo esperado en caja de 58.400.
El producto objetivo comienza con stock cero: la venta con inventario negativo ya
está permitida; no se altera esa regla. El producto usado para cambios de unidad o
borrado no tiene historial ni unidad bloqueada.

No se duplicaron los casos existentes de transferencia, precio cambiado o producto
inactivo, ni se agregaron pruebas de descuentos para elevar la puntuación.

## Ejecución

Base configurada explícitamente: `frubberpos_test`, localhost:5432, rol de pruebas
`frubber`; las pruebas generales existentes también crean bases `frubberpos_scratch_*`.
No se usaron bases del negocio.

1. Pruebas afectadas en Release: **28 correctas, 0 fallidas**. Filtro:
   `# MISIÓN 05 — Diagnóstico del fallo PostgreSQL 42501

Proyecto: Frubber POS

**Objetivo:** investigar y resolver el fallo de infraestructura que impidió obtener una suite completa en verde durante la Misión 04.

## Tareas

1. Ejecutar únicamente la prueba `FailureAfterBaseline_RollsBackSchemaAndLedger_ThenCanRetry` para determinar si el problema es reproducible.
2. Inspeccionar `ScratchDatabase.DisposeAsync` y la operación `DROP DATABASE WITH (FORCE)`.
3. Identificar qué conexiones permanecen abiertas, qué roles las poseen y por qué PostgreSQL impide terminarlas.
4. Determinar si el problema corresponde a conexiones sin liberar, permisos o interferencia entre pruebas.
5. Proponer la corrección mínima y segura, respetando el aislamiento de bases temporales.
6. Si se confirma una corrección necesaria en la infraestructura de pruebas, implementarla únicamente allí y verificarla mediante la prueba afectada y posteriormente la suite completa.

## Restricciones

- No modificar reglas de negocio ni funcionalidades de producción.
- No conceder permisos de superusuario ni privilegios amplios como solución rápida.
- No intervenir bases de datos comerciales.
- No ejecutar nuevamente Stryker.NET.
- No modificar pruebas de cobro ni descuentos.
- Documentar cualquier cambio antes de cerrar la misión.

## Entregable

Presentar causa identificada, evidencia, archivos modificados, resultados de la prueba afectada y resultado final de la suite.

Si el problema no puede reproducirse, reportarlo como posible fallo intermitente y no inventar una corrección.

**Objetivo de cierre:** recuperar una suite completa sin fallos, manteniendo documentadas las cinco pruebas omitidas de infraestructura 2.0.FullyQualifiedName~FinalizeSaleValidationTests|FullyQualifiedName~FinalizeSaleUseCaseTests|FullyQualifiedName~SaleRequiresOpenCashTests`.
2. Suite completa en Release: **775 totales; 769 correctas, 1 fallida, 5 omitidas**.
   Los 14 casos nuevos también pasaron en esta ejecución. No hay advertencias nuevas;
   permanecen dos avisos xUnit2013 de ProductsViewModelTests, ajenos al cambio.
3. Una ejecución efectiva de Stryker.NET 5.1.0, concurrency=1, solo los 14 casos nuevos.
   Previamente hubo un rechazo de configuración por usar `output` en JSON; se trasladó
   a `--output` antes de que empezara cualquier experimento de mutaciones.

La primera invocación ordinaria de dotnet no pudo operar bajo el aislamiento de
Windows; las ejecuciones con resultados se realizaron fuera de ese aislamiento.

### Fallo de la suite completa

`Postgres20BaselineInstallerTests.FailureAfterBaseline_RollsBackSchemaAndLedger_ThenCanRetry`
falló al destruir su base desechable, en `ScratchDatabase.DisposeAsync`:
PostgreSQL **42501**, permiso denegado para terminar el proceso durante DROP DATABASE
WITH (FORCE). No se modificó esa prueba ni se otorgaron permisos. El resultado anterior
de Misión 02 registraba 756 correctas y 5 omitidas; por tanto, no se afirma que este
fallo ya existiera en aquella ejecución ni se declara la suite actual en verde.

## Confirmación de mutaciones

Se usó el filtro por posiciones de caracteres documentado por
[Stryker.NET](https://stryker-mutator.io/docs/stryker-net/configuration/), aplicado
únicamente a las validaciones de FinalizeSaleUseCase: líneas 137–150, 165–168,
205–210, 224–235 y 242–247. Posiciones: 5076..5783, 6378..6547,
8048..8366, 8836..9383 y 9613..9950, sobre el archivo original con CRLF.

| ID de Misión 03 (conservado en este JSON) | Estado nuevo | Prueba que lo detecta |
|---|---|---|
| 729 — efectivo insuficiente | Killed | Cash_ReceivedBelowTotal_RejectsWithoutChanges_ThenExactPaymentSucceeds (ambos casos) |
| 659 — efectivo obligatorio | Killed | Cash_MissingOrNegativeReceived_RejectsWithoutChanges (ambos casos) |
| 649 — venta vacía | Killed | EmptySale_RejectsWithoutChanges |
| 655 — medio inválido | Killed | UnsupportedPayment_RejectsWithoutChanges |
| 665 — mensaje de efectivo obligatorio | Killed | Cash_MissingOrNegativeReceived_RejectsWithoutChanges |
| 681 — sesión ausente | Killed | SignedOut_RejectsWithoutChanges |
| 705 — producto eliminado | Killed | CatalogChangedAfterDraft_RejectsWithoutFurtherChanges (deleted) |
| 714 — unidad cambiada | Killed | CatalogChangedAfterDraft_RejectsWithoutFurtherChanges (unit) |
| 720 — PLU/nombre cambiado | Killed | CatalogChangedAfterDraft_RejectsWithoutFurtherChanges (plu/name) |

Resultado del archivo en el JSON: **38 Killed, 2 Survived, 0 NoCoverage,
71 Ignored y 3 CompileError**. Se ejecutaron 40 mutantes; puntuación acotada **95 %**.
Los ignorados no cuentan como detectados ni como ejecutados.

Supervivientes de esta pasada, deliberadamente sin corregir:

- **723**: `subtotal - DiscountCop` pasa a suma. Los casos nuevos usan descuento cero.
- **727**: `total <= 0` pasa a `total < 0`. Los casos nuevos no ejercitan descuento total.

Se relacionan con descuentos/total cero, fuera del comportamiento comercial autorizado
en esta misión. Esta pasada con filtro de pruebas no demuestra que sobrevivan a la suite
completa: no se deben confundir sus resultados con los de Misión 03.
Tampoco se repitió AdjustStock ni se reevaluaron sus mutantes de robustez.

## Integridad y evidencia local

SHA256 de los **242 archivos de producción**: sin cambios antes/después de Stryker.
FinalizeSaleUseCase mantiene el SHA256
`B9397CC63F07247CCD9AE1EAB60D70479E3135580384202D65C7330E24881EF6`,
y coincide con el código fuente de los informes originales de Misión 03.

Artefactos locales, bajo `artifacts/mision-04/` (ignorados por Git):

- `mision-04-affected.trx`, `mision-04-full.trx`.
- `stryker-config.json` (configuración experimental, no integrada).
- `stryker/reports/mutation-report.json` y `mutation-report.html`.
- `production-hashes-before.json` y `production-hashes-verification.json`.

## Pendientes

Los dos huecos críticos y las siete ramas señaladas quedan detectados por mutaciones.
No se demostró un defecto del código de producción en esos escenarios.
Queda pendiente resolver o explicar el fallo de permisos de la prueba de migraciones
para obtener una suite completa verde. JHO-139 permanece In Progress por esa validación
pendiente; no se cierra como si la suite hubiera pasado. Se detiene la misión con este informe.
