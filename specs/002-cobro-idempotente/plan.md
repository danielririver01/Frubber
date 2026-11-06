# Plan: recuperar un cobro tras un corte de conexión

**Rama real**: `arena/04894ea8-frubber` | **Fecha**: 2026-11-06 | **Spec**: [spec.md](spec.md)
**Issues**: JHO-114 y JHO-136. El tramo SQL no cierra ninguno por sí solo.

## Resumen

Agregar un prototipo de función PostgreSQL que serializa solicitudes por UUID en la misma transacción que la venta existente y devuelve el resultado previo solo para el mismo puesto y solicitud. Probarlo directamente bajo roles reales en un clúster aislado, incluidos corte de conexión y competencia por UUID. Después conectar la aplicación y guardar el intento de forma durable bajo un diseño aprobado y pruebas Windows; no habilitar flujos parciales.

## Contexto técnico

C#/.NET 10 WPF, Npgsql, Dapper y PostgreSQL 18.6. Baseline `database/postgresql/001_Baseline.sql`; seguridad 002 y venta 003 aún no conectadas. El registro de solicitud se guarda como JSONB con igualdad estructural; objetos JSON con orden de propiedades diferente son equivalentes, pero el orden de líneas y todos los valores forman parte del intento. El bloqueo único y de fila evita doble ejecución y se revierte con la venta. Repeticiones aceptadas usan el mismo rol/puesto y contenido exacto.

## Puertas de constitución

- No modificar bases de clientes ni deshabilitar disparadores. Pruebas solo en un clúster dedicado y desechable.
- Sin acceso directo del puesto a registros de solicitud, venta o contador. La versión provisional anterior de venta deja de ser ejecutable directamente por el puesto en el experimento.
- No afirmar autorización del cajero: identidad de puesto no equivale a sesión PIN, y licencia y TLS siguen pendientes.
- No cerrar el issue por pruebas SQL directas. El cliente debe persistir el intento, recuperar resultados y pasar pruebas de aplicación y Windows.

## Estructura

- `database/postgresql/004_CobroIdempotente.sql`: prototipo transaccional sin integración automática.
- `prototipo/prueba-cobro-idempotente-aislada.py`: pruebas en clúster desechable con roles/credenciales de prueba; el guion 002 crea roles globales y requiere una base de nombre fijo, por eso se prohíbe en un clúster compartido.
- `specs/002-cobro-idempotente/tasks.md`: seguimiento y evidencia sin completar pruebas no realizadas.

## Restricciones y riesgos

La línea 004 experimental rechaza bases con ventas existentes y requiere administración exclusiva sin puestos activos: no es una migración de clientes. La identidad de cajero no se vincula al PIN; la repetición no revalida precios o apertura de caja para poder recuperar el comprobante original. La identidad de puesto sí debe estar activa y coincidir. La misma solicitud debe reproducirse íntegra; no guardar contraseñas ni PIN en el registro. Al perderse COMMIT el cliente no sabe si se ejecutó; reintentar con el mismo identificador, nunca uno nuevo.

## Transición pendiente a la aplicación

El código de caja actual llama `FinalizeSaleUseCase` con líneas y pago, sin UUID; abre la caja global, revalida catálogo, numera e inserta por separado en el esquema A. `TpvViewModel` mantiene el borrador en memoria y permite volver a intentar después de un fallo. Una desconexión durante la confirmación puede dejar resultado incierto: la frase «la venta nunca se registra» de su comentario no es una garantía. **No integrar ni liberar el flujo B hasta reemplazar esa ruta y proteger los reintentos.**

El flujo B necesita guardar de forma durable y atómica, antes de cualquier envío, UUID, identidad estable del servidor/base y puesto, caja, cajero, líneas ordenadas y datos de pago. El diseño definitivo debe resolver bloqueo entre procesos, escritura interrumpida, versión/formato, confidencialidad y permisos Windows, pérdida/corrupción/respaldos y qué hacer si cambió la base; no debe guardar PIN, hash ni contraseña. Se bloquean ediciones y cobros nuevos que puedan duplicar el intento mientras el resultado sea incierto. Tras éxito confirmado se conserva suficiente estado para presentar el resultado incluso si la aplicación cae antes de limpiar; recuperación/limpieza debe ser idempotente. Una restauración del servidor a un punto anterior puede perder tanto venta como historial y requiere un procedimiento de conciliación: el UUID por sí solo no es un respaldo.

La recuperación debe reusar los datos exactos, no pasar primero por validación de precio/caja que pueda impedir consultar un cobro confirmado. Un resultado devuelto por la función dentro de una transacción todavía no confirma `COMMIT`. Fallos de red durante el envío o la confirmación, cancelación y reintentos por errores de serialización se tratan sin cambiar UUID, y se necesitan límites de tiempo y estados claros en interfaz. La respuesta experimental devuelve ID, número y total, no el ticket completo; se requiere consulta segura de cabecera y líneas históricas, compatible con el puesto y el modelo de permisos, y resolver diferencias entre `timestamptz` de B y fecha/offset usados por el ticket actual. El diseño de sesión autenticada y licencia debe incluir tanto venta nueva como recuperación sin convertir la repetición en una vía de evasión de AD-03.

La prueba fue en READ COMMITTED; aislamientos superiores pueden requerir reintentar transacciones por serialización, siempre con el mismo intento. La protección supone que los permisos de los roles se verifican en la instalación final y que nadie otorga acceso directo a la función anterior por otros roles. Un servidor en mantenimiento exclusivo es requisito, no una comprobación completa automatizada del prototipo.
