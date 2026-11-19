# Diseño de base de datos de Frubber POS 2.0

Versión 2.0 · 19 de noviembre de 2026

- **B-1.** La línea base de PostgreSQL es [001_Baseline.sql](../../database/postgresql/001_Baseline.sql). Las migraciones SQLite son historia de la 1.0. La migración de datos usa la herramienta de JHO-124 y verifica el cuadre antes de confirmar.
- **B-2.** Las ventas referencian sesiones de caja y las sesiones referencian puestos. El índice único parcial permite una sola caja abierta por puesto. El historial anterior queda en el puesto desconocido de la 1.0. La clave de solicitud de venta es UUID única. El contador de tickets se bloquea por fila y participa en la misma transacción que la venta.
- **B-3.** El servidor genera el respaldo diario con pg_dump y lo restaura mediante pg_restore en un destino dedicado. El servidor verifica un respaldo previo antes de migrar.
- El motor usa booleanos, fechas con zona horaria y enteros de 64 bits para dinero en COP y cantidades en milésimas. Los disparadores PL/pgSQL protegen ventas, anulaciones y cajas. El puesto tiene permisos mínimos y carece de escritura directa en tablas críticas. El PIN se verifica mediante funciones SECURITY DEFINER.
- La licencia se persiste como texto firmado. El esquema, las funciones de seguridad y los guiones operativos viven en el repositorio. Las revisiones se ejecutan desde el servidor.
