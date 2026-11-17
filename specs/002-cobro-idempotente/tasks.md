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

- [x] T007a [US1] Prototipar y probar consulta histórica de cabecera y líneas restringida al puesto original; no sustituye autorización de cajero ni integración.
- [x] T008 Diseñar e implementar en la aplicación la persistencia durable del identificador y del intento sin contraseñas/PIN, y recuperación tras reinicio; no asignar un UUID nuevo ante resultado incierto.
  - [x] T008a Implementar y probar el almacenamiento local en Windows. El registro usa DPAPI por usuario y guarda UUID, identidad de base, puesto, caja, cajero y contenido exacto. La escritura descarga los datos a disco antes de publicar el archivo. Un intento pendiente impide crear otro y el comprobante confirmado sobrevive a la reapertura del registro.
  - [x] T008b Aprovisionar una identidad estable de la base y conectar el registro al arranque Windows de laboratorio. El cliente vincula el intento a la base, puesto y cajero originales y bloquea la caja si el registro es ilegible. La prueba cerró el proceso, reinició y recuperó el cobro.
  - [x] T008c Conectar el registro al modelo de puesto de Windows. Mientras hay un intento pendiente se bloquean edición, cobro nuevo y cierre. Se conserva el comprobante hasta que el operador confirma que lo vio.
- [x] T009 Integrar autorización de cajero por PIN y licencia con la sesión y las operaciones. `010_RecuperacionAutenticada.sql` valida la sesión contra el puesto, usuario activo, vigencia e identidad de base. Un cambio de PIN invalida la sesión anterior. El punto de entrada del experimento sin PIN se revoca al rol de puesto. Pasaron los rechazos con sesión ausente y con PIN de otro cajero. El ejecutable usó FRB2 firmada.
- [x] T010 Ejecutar flujo de cobro y corte con Npgsql y la aplicación, pruebas de permisos y concurrencia entre puestos y regresión/build Windows; registrar evidencia en Linear.
  - [x] T010a Comprobar el modelo WPF con Npgsql y PostgreSQL 18.6 temporal sobre TLS. La prueba terminó la conexión del puesto durante el INSERT de una línea y comprobó reversión. En un segundo escenario se descartó la respuesta de un cobro confirmado a la entrada del cliente. En cada escenario se recreó el modelo de puesto y se reabrió el registro DPAPI con la caja y el UUID originales. Dos cobros dieron dos ventas, dos números seguidos y dos descuentos de inventario.
  - [x] T010b Ejecutar el binario Release como proceso separado, cortar la respuesta TCP después de confirmar y reiniciarlo. Con TLS verificado y licencia FRB2 firmada, recuperó V-000004 después del corte y conservó una sola venta y un solo descuento. También recuperó V-000003 después de que el servidor interrumpió la conexión antes de confirmar. Las cuatro ventas del ensayo usaron los consecutivos 1–4 y descontaron 4.000 milésimas en total.
  - [x] T010c Windows Release, 0 errores y dos avisos xUnit preexistentes. La regresión completa pasó en PostgreSQL desechable con 752 aprobadas, 0 fallos y 3 pruebas optativas omitidas. De esas tres, la integración de recuperación y la de cajas independientes pasaron por separado. La prueba anterior de corte durante la confirmación del instalador conserva la evidencia de la especificación 001.
- [x] T011 Revisar los criterios de JHO-114 frente a los resultados de corte y reintento. Se recuperaron los mismos intentos sin ventas duplicadas ni pérdidas en los escenarios probados. El cierre corresponde al flujo en laboratorio. La incorporación al instalador y el manejo de bases de clientes siguen sus tareas de despliegue.

## Evidencia del tramo SQL (2026-11-06)

- Banco `prototipo/prueba-cobro-idempotente-aislada.py`, PostgreSQL 18.6 local, psycopg 3, aislamiento por defecto READ COMMITTED. Dos roles de puesto, diez conexiones; 500 cobros concurrentes numerados 5–504 sin huecos, 505 ventas/líneas/intentos completos al final e inventarios coherentes.
- Un mismo intento en diez conexiones dio el mismo ID, número y total; una carrera por UUID entre dos puestos tuvo un ganador y un rechazo sin datos. Rollback y fallo inyectado no dejaron intento ni consumieron número; un competidor esperó y reutilizó el número 2.
- Proxy TCP local sin TLS dejó pasar `COMMIT`, descartó la respuesta de PostgreSQL y psycopg observó desconexión. Reintento idéntico recuperó ID/número/total de la venta 505 sin repetir inventario. Esto **no** prueba Npgsql ni persistencia del cliente.
- Se verificaron el rechazo al activar sobre venta preexistente, escritura/lectura directa y función anterior, datos alterados, puesto distinto/inactivo, caja cerrada y datos de línea no permitidos. Base y roles temporales borrados; servidor aislado.
- El cobro devuelve ID, número y total; una consulta experimental separada recupera cabecera y líneas históricas solo al puesto activo original. Se probó con caja cerrada, cambio de precio, dos líneas ordenadas y rechazo a otro puesto. Falta enlazar autenticación de cajero y licencia y adaptar el ticket real. No demuestra seguridad completa ni cierre del issue.
