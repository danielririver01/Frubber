# Tareas: preparación segura de una base nueva 2.0

**Entrada**: [spec.md](spec.md), [plan.md](plan.md) | **Issue**: JHO-136
Una tarea de código queda abierta hasta que exista evidencia suficiente; escribir una prueba no significa que haya pasado.

## Preparación y fundamentos

- [x] T001 Registrar la línea base aprobada, límites y principios en `.specify/memory/constitution.md` y `specs/001-inicializacion-postgresql20/spec.md`.
- [x] T002 Registrar plan y restricciones en `specs/001-inicializacion-postgresql20/plan.md`.

## Historia 1 — Preparación transaccional

- [ ] T003 [US1] Verificar en Windows el recurso SQL embebido y la instalación/repetición de `src/FrubberPOS.Infrastructure/Migrations/Postgres20BaselineInstaller.cs` mediante `tests/FrubberPOS.Tests/Infrastructure/Migrations/Postgres20BaselineInstallerTests.cs`.
- [ ] T004 [US1] Agregar y ejecutar una prueba del ejecutor C# que inyecte fallo después de iniciar la aplicación del guion y antes de confirmar; verificar que esquema y registro reviertan.

## Historia 2 — Proteger información existente

- [ ] T005 [US2] Reforzar y probar el rechazo de objetos ajenos no cubiertos por la detección inicial en `src/FrubberPOS.Infrastructure/Migrations/Postgres20BaselineInstaller.cs` y `tests/FrubberPOS.Tests/Infrastructure/Migrations/Postgres20BaselineInstallerTests.cs`.
- [ ] T006 [US2] Ejecutar pruebas C# de bases existentes, historial incompatible y candado de `src/FrubberPOS.Infrastructure/Migrations/MigrationRunner.cs`; comparar su contenido antes y después.

## Historia 3 — Repetición y fallos

- [ ] T007 [US3] Ejecutar prueba C# de dos instaladores concurrentes sobre base temporal y comprobar un único registro.
- [ ] T008 [US3] Agregar y ejecutar prueba de resultado incierto al interrumpir confirmación, sin afirmar que se revirtió.

## Cierre del tramo

- [ ] T009 Ejecutar build Release y regresión de migraciones en Windows; registrar comandos, versión, resultados y límites en `prototipo/JHO-136-integracion-postgresql.md` y Linear.
- [ ] T010 Comparar resultado con `spec.md`, `plan.md` y esta lista; registrar brechas antes de considerar el tramo convergente.
- [ ] T011 Verificar que no se conectó el instalador ni `002_Security.sql` a los puestos y que las pruebas solo usan destinos desechables; mantener estos candados en `prototipo/JHO-136-integracion-postgresql.md`.
- [ ] T012 Revisar limitaciones del catálogo de objetos antes de considerar satisfecho RF-004; una base dedicada y vacía sigue siendo requisito, y la comprobación parcial por sí sola no demuestra que todo objeto posible sea detectado.

**Evidencia anterior**: el protocolo SQL emulado con Python pasó en PostgreSQL 18.6, pero no ejecutó C#; ver `prototipo/JHO-136-integracion-postgresql.md`. El código inicial y la clase xUnit existen sin compilación ni ejecución .NET observadas. El 2026-11-06 se identificó y corrigió en SQL la omisión de collation y se agregaron verificaciones SQL aisladas de otros objetos en `prototipo/prueba-inicializador-jho136.py`; T005 continúa abierta hasta ejecutar las pruebas del instalador C# y revisar los límites de cobertura. JHO-136, JHO-114 y los candados de seguridad/licencia/migración siguen abiertos según sus propios criterios.

**Implementación en curso (2026-11-06)**: se añadieron pruebas C# aún no ejecutadas de fallo tras el guion y de confirmación con respuesta simulada perdida. El instalador ahora clasifica como inciertos errores y cancelación durante el intento de confirmación. Una prueba PostgreSQL/psycopg con proxy TCP local observó una confirmación realizada por el servidor pese al fallo de conexión del cliente; no ejecuta Npgsql. T004 y T008 siguen abiertas hasta ejecutar el ejecutor real, probar un corte con Npgsql y verificar la liberación de conexión tras el fallo.

**Seguridad adicional (2026-11-06)**: se identificó que `public` antes de `pg_catalog` podía ejecutar una función homónima de una base ajena. Se cambió la resolución de nombres y se agregaron verificaciones de historial como tabla permanente sin seguridad por fila. El banco SQL adversarial pasó en PostgreSQL aislado; T005/T006/T012 siguen abiertas hasta comprobar el código C#, revisar integridad de históricos y completar la suite correspondiente. El 06-nov el dueño autorizó agrupar pruebas Windows, no eliminarlas ni cerrar el issue sin evidencia.
