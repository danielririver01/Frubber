# Tareas: cobro idempotente

**Entrada**: [spec.md](spec.md) y [plan.md](plan.md) | **Issue**: JHO-114

## Prototipo aislado

- [x] T001 Definir escenarios, límites y plan del tramo en `specs/002-cobro-idempotente/`.
- [x] T002 [US1] Implementar registro transaccional por intento y función de reintento en `database/postgresql/004_CobroIdempotente.sql`.
- [x] T003 [US2] Implementar rechazo de contenido diferente, puesto distinto y funciones o tablas sin autorización en `database/postgresql/004_CobroIdempotente.sql`.
- [x] T004 [US3] Rechazar instalación experimental en bases con ventas existentes y documentar necesidad de exclusividad.
- [x] T005 [US1] Probar reintento idéntico, rollback y concurrencia con roles de puesto reales en `prototipo/prueba-cobro-idempotente-aislada.py`.
- [x] T006 [US1] Probar corte TCP durante confirmación de una venta y recuperación del mismo resultado con el mismo UUID en `prototipo/prueba-cobro-idempotente-aislada.py`.
- [x] T007 [US2] Verificar denegación de lectura/escritura directa y rechazo de solicitud manipulada o de otro puesto, incluidas carreras.

## Integración antes del cierre

- [ ] T008 Diseñar e implementar en la aplicación la persistencia durable del identificador y del intento sin contraseñas/PIN, y recuperación tras reinicio; no asignar un UUID nuevo ante resultado incierto.
- [ ] T009 Integrar autorización de cajero por PIN y licencia con la sesión y las operaciones; no activar el prototipo sin los candados de AD-03.
- [ ] T010 Ejecutar flujo de cobro y corte con Npgsql y la aplicación, pruebas de permisos y concurrencia entre puestos y regresión/build Windows; registrar evidencia en Linear.
- [ ] T011 Ejecutar análisis de coherencia y convergencia entre especificación, tareas, código y pruebas; no cerrar JHO-114 ni JHO-136 hasta sus criterios reales.

## Evidencia del tramo SQL (2026-11-06)

- Banco `prototipo/prueba-cobro-idempotente-aislada.py`, PostgreSQL 18.6 local, psycopg 3, aislamiento por defecto READ COMMITTED. Dos roles de puesto, diez conexiones; 500 cobros concurrentes numerados 5–504 sin huecos, 505 ventas/líneas/intentos completos al final e inventarios coherentes.
- Un mismo intento en diez conexiones dio el mismo ID, número y total; una carrera por UUID entre dos puestos tuvo un ganador y un rechazo sin datos. Rollback y fallo inyectado no dejaron intento ni consumieron número; un competidor esperó y reutilizó el número 2.
- Proxy TCP local sin TLS dejó pasar `COMMIT`, descartó la respuesta de PostgreSQL y psycopg observó desconexión. Reintento idéntico recuperó ID/número/total de la venta 505 sin repetir inventario. Esto **no** prueba Npgsql ni persistencia del cliente.
- Se verificaron el rechazo al activar sobre venta preexistente, escritura/lectura directa y función anterior, datos alterados, puesto distinto/inactivo, caja cerrada y datos de línea no permitidos. Base y roles temporales borrados; servidor aislado.
- El prototipo devuelve ID, número y total; aún falta una ruta autorizada para recuperar todos los datos y líneas del comprobante. No demuestra seguridad completa ni cierre del issue.
