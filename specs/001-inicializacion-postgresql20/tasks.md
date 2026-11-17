# Tareas: preparación segura de una base nueva 2.0

**Entrada**: [spec.md](spec.md), [plan.md](plan.md) | **Issue**: JHO-136
Una tarea de código queda abierta hasta que exista evidencia suficiente; escribir una prueba no significa que haya pasado.

## Preparación y fundamentos

- [x] T001 Registrar la línea base aprobada, límites y principios en `.specify/memory/constitution.md` y `specs/001-inicializacion-postgresql20/spec.md`.
- [x] T002 Registrar plan y restricciones en `specs/001-inicializacion-postgresql20/plan.md`.

## Historia 1 — Preparación transaccional

- [x] T003 [US1] Verificar en Windows el recurso SQL embebido y la instalación/repetición de `src/FrubberPOS.Infrastructure/Migrations/Postgres20BaselineInstaller.cs` mediante `tests/FrubberPOS.Tests/Infrastructure/Migrations/Postgres20BaselineInstallerTests.cs`.
- [x] T004 [US1] Agregar y ejecutar una prueba del ejecutor C# que inyecte fallo después de iniciar la aplicación del guion y antes de confirmar; verificar que esquema y registro reviertan.

## Historia 2 — Proteger información existente

- [x] T005 [US2] Reforzar y probar el rechazo de objetos ajenos no cubiertos por la detección inicial en `src/FrubberPOS.Infrastructure/Migrations/Postgres20BaselineInstaller.cs` y `tests/FrubberPOS.Tests/Infrastructure/Migrations/Postgres20BaselineInstallerTests.cs`.
- [x] T006 [US2] Ejecutar pruebas C# de bases existentes, historial incompatible y candado de `src/FrubberPOS.Infrastructure/Migrations/MigrationRunner.cs`; comparar su contenido antes y después.

## Historia 3 — Repetición y fallos

- [x] T007 [US3] Ejecutar prueba C# de dos instaladores concurrentes sobre base temporal y comprobar un único registro.
- [x] T008 [US3] Agregar y ejecutar prueba de resultado incierto al interrumpir confirmación, sin afirmar que se revirtió.

## Cierre del tramo

- [x] T009 Ejecutar build Release y regresión de migraciones en Windows; registrar comandos, versión, resultados y límites en `prototipo/JHO-136-integracion-postgresql.md` y Linear.
- [x] T010 Comparar resultado con `spec.md`, `plan.md` y esta lista; registrar brechas antes de considerar el tramo convergente.
- [x] T011 Verificar que no se conectó el instalador ni `002_Security.sql` a los puestos y que las pruebas solo usan destinos desechables; mantener estos candados en `prototipo/JHO-136-integracion-postgresql.md`.
- [x] T012 Revisar limitaciones del catálogo de objetos antes de considerar satisfecho RF-004; una base dedicada y vacía sigue siendo requisito, y la comprobación parcial por sí sola no demuestra que todo objeto posible sea detectado.

**Evidencia del 2026-11-10**: build Release y 36 pruebas de migraciones pasaron en Windows x64 con PostgreSQL 18.6 desechable. La prueba nueva cortó el TCP antes de que Npgsql recibiera la confirmación. El instalador devolvió un estado incierto, una conexión directa verificó el registro y el reintento devolvió cero aplicaciones. Los resultados y límites están en `prototipo/JHO-136-integracion-postgresql.md`. JHO-136 y JHO-114 siguen abiertos.
