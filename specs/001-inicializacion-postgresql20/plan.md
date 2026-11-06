# Plan: preparación segura de una base nueva 2.0

**Rama real**: `arena/04894ea8-frubber` | **Fecha**: 2026-11-06 | **Especificación**: [spec.md](spec.md)
**Issue**: JHO-136, parte acotada; no implica cerrar el issue.

## Resumen

Conservar el instalador aislado de línea base y el candado del migrador anterior; reforzar la detección de objetos ajenos, agregar pruebas verificables y no conectar la preparación al arranque de la aplicación hasta cumplir las dependencias de seguridad y de integración.

## Contexto técnico

- C#/.NET 10, Npgsql y xUnit; aplicación WPF de Windows. PostgreSQL 18.6 para evidencia previa.
- `database/postgresql/001_Baseline.sql` es el recurso embebido; `schema_migration` registra nombre y SHA-256 de texto. El historial anterior es `SchemaMigration`.
- El instalador `Postgres20BaselineInstaller` opera en una transacción y usa bloqueo consultivo de transacción; no instala `002_Security.sql` ni `003_RegistroVenta.sql`.
- Los recursos de prueba son bases aleatorias de un servidor PostgreSQL aislado. Windows/.NET no está disponible en este entorno.

## Revisión de la constitución

- **Preservación**: sin actualización, borrado o conversión de bases existentes; pruebas solo en clúster desechable.
- **Seguridad**: no dar permisos nuevos ni activar rutas 2.0. El guion 002 contiene operaciones globales y no se automatiza.
- **Transacción**: esquema y registro juntos; reconocer el resultado incierto durante commit.
- **Evidencia**: la prueba SQL con Python no sustituye la ejecución C#/xUnit; el build y la regresión Windows quedan pendientes.
- **Rama**: el directorio `specs/001-...` no crea rama ni cambia la actual.

## Diseño y estructura

- `src/FrubberPOS.Infrastructure/Migrations/Postgres20BaselineInstaller.cs`: precondiciones, bloqueo, aplicación y registro.
- `src/FrubberPOS.Infrastructure/Migrations/MigrationRunner.cs`: rechaza historial 2.0 antes del catálogo anterior.
- `tests/FrubberPOS.Tests/Infrastructure/Migrations/Postgres20BaselineInstallerTests.cs`: pruebas sobre bases aleatorias; incluir objetos preexistentes menos comunes.
- `prototipo/JHO-136-integracion-postgresql.md`: evidencias y límites.
- `specs/001-inicializacion-postgresql20/tasks.md`: trabajo y validación pendientes.

## Riesgos y decisiones

El catálogo de objetos de PostgreSQL incluye más que tablas, funciones y tipos; rechazar también objetos de esquema detectables por dependencias en el catálogo y tipos de objetos sin esquema identificados, sin afirmar que el examen sustituye el aislamiento y el destino dedicado. Si se interrumpe COMMIT, consultar el historial antes de continuar. El checksum exacto depende de los finales de línea del recurso; `.gitattributes` fuerza CRLF para SQL. No inferir autorización de usuario por un ID activo.

## Puertas de entrega

Revisar pruebas de objetos no ordinarios, fallo antes de commit y concurrencia; ejecutar la clase xUnit y regresiones del migrador en Windows, además del build. Mantener la aplicación y 002 desconectados hasta que JHO-136 cumpla sus requisitos completos. Revaluar constitución y especificación después de cada hallazgo; no marcar como completado lo no ejecutado.

### Mitigación de resolución de nombres

El mecanismo no confía en funciones ni catálogos definidos en `public`: califica llamadas y tablas de `pg_catalog`, mantiene `public` como esquema de creación y rechaza vistas o tablas con seguridad por fila como historial. Los controles son defensas parciales, no autenticación del esquema. Ensayos SQL adversariales aislados no sustituyen xUnit, auditoría de seguridad ni pruebas Windows.
