# Plan: recuperar un cobro tras un corte de conexión

**Rama real**: `arena/04894ea8-frubber` | **Fecha**: 2026-11-06 | **Spec**: [spec.md](spec.md)
**Issues**: JHO-114 y JHO-136. El tramo SQL no cierra ninguno por sí solo.

## Resumen

Agregar un prototipo de función PostgreSQL que serializa solicitudes por UUID en la misma transacción que la venta existente y devuelve el resultado previo solo para el mismo puesto y solicitud. Probarlo directamente bajo roles reales en un clúster aislado, incluidos corte de conexión y competencia por UUID. Después conectar la aplicación y guardar el intento de forma durable bajo un diseño aprobado y pruebas Windows; no habilitar flujos parciales.

## Contexto técnico

C#/.NET 10 WPF, Npgsql, Dapper y PostgreSQL 18.6. JHO-136 conectó el inicio `--puesto`, seguridad 002, venta 003, perfil DPAPI, TLS verificado y licencia de la 2.0. El registro de solicitud de 004 se guarda como JSONB con igualdad estructural. El orden de líneas y todos los valores forman parte del intento. El bloqueo único y de fila evita doble ejecución y se revierte con la venta. Repeticiones aceptadas usan el mismo rol/puesto y contenido exacto.

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

En la composición 2.0, `WorkstationViewModel` usa `FinalizeWorkstationSaleUseCase`, que genera un UUID nuevo por ejecución. `Postgres20WorkstationStore` resuelve la caja propia al enviar a `app_registrar_venta`. Si hay resultado incierto, el modelo bloquea el borrador en memoria. Reiniciar pierde ese bloqueo. La integración de JHO-114 se realiza en esta composición; la ruta de la 1.0 queda fuera del alcance.

### Orden de integración

1. Definir el contrato de intento persistido con UUID, puesto, identidad de la base aprovisionada, ID de caja, cajero, líneas y pago. Capturar el ID de caja al crear el intento en vez de consultar la caja abierta al reintentar. Guardar un único intento pendiente por puesto de forma atómica bajo el perfil Windows protegido.
2. En el arranque, leer y validar el intento antes de habilitar otro cobro o cierre. En caso de autenticación de otro cajero, cambio de perfil o almacenamiento ilegible, mostrar un estado de revisión sin enviar ni sobrescribir el intento.
3. Vincular la identidad del cajero verificada por PIN a la función SQL que autoriza cobrar o recuperar, y conservar la revalidación de licencia. El enlace de autorización debe comprobar puesto y cajero aun cuando el intento ya se haya confirmado.
4. Integrar la ruta bajo ensayo aislado. En error incierto, conservar el intento y permitir consultar o repetir la misma solicitud. Al recibir el resultado confirmado, persistir el resultado antes de limpiar el intento, de modo que sea posible mostrarlo aunque el proceso se cierre entre ambos pasos.
5. Ejecutar dos cortes controlados, uno antes de confirmar y otro tras confirmar y antes de recibir respuesta, recreando el cliente entre envíos. Comparar respuesta, números, inventario y cantidad de ventas. Mantener la prueba de solicitudes a la vez y de identidad incorrecta.

El flujo B necesita guardar de forma durable y atómica, antes de cualquier envío, UUID, identidad estable del servidor/base y puesto, caja, cajero, líneas ordenadas y datos de pago. El diseño definitivo debe resolver bloqueo entre procesos, escritura interrumpida, versión/formato, confidencialidad y permisos Windows, pérdida/corrupción/respaldos y qué hacer si cambió la base; no debe guardar PIN, hash ni contraseña. Se bloquean ediciones y cobros nuevos que puedan duplicar el intento mientras el resultado sea incierto. Tras éxito confirmado se conserva suficiente estado para presentar el resultado incluso si la aplicación cae antes de limpiar; recuperación/limpieza debe ser idempotente. Una restauración del servidor a un punto anterior puede perder tanto venta como historial y requiere un procedimiento de conciliación: el UUID por sí solo no es un respaldo.

La recuperación repite los datos exactos y usa el mismo UUID, puesto, cajero y caja. Un fallo de red o una cancelación conserva el registro local. El operador confirma el comprobante en pantalla para eliminar el registro. La respuesta de recuperación contiene ID, número y total de la venta. La impresión del ticket y la conciliación administrativa de casos que requieren intervención quedan fuera de este flujo de recuperación.

El guion 010 asigna a la base un UUID persistente y habilita `app_sale_submit` para cobrar o recuperar. La función comprueba una sesión emitida al validar el PIN, ligada al puesto y al cajero. Se revoca al puesto la llamada directa al prototipo de cobro y al comprobante de 004. El cliente revalida la licencia antes de enviar o recuperar. El punto de entrada WPF `--puesto-recuperacion-lab <perfil DPAPI>` es para el banco de laboratorio sobre una base recién preparada. La activación en instalaciones existentes sigue dependiendo de las tareas de migración y empaquetado.

Para reproducir los cortes en el ejecutable Release, usar un clúster desechable con la configuración TLS de la prueba de dos puestos y `FRUBBER_JHO114_RECOVERY_ONLY=1`. En una sesión interactiva de Windows, indicar en `FRUBBER_JHO114_SIGNED_LICENSE_PATH` la ruta de una FRB2 de laboratorio válida y ejecutar el filtro `RecoversExperimentalSaleAfterLostResponseAndNewWorkstationModel`. El ensayo escribe un perfil DPAPI temporal y usa una copia de la base. El corte antes de confirmar termina la sesión del motor durante el INSERT. El corte después de confirmar utiliza un relé local que conserva TLS. El motor confirma la venta mientras el relé retiene la respuesta y el relé cierra la conexión antes de que el ejecutable la reciba.

La prueba fue en READ COMMITTED; aislamientos superiores pueden requerir reintentar transacciones por serialización, siempre con el mismo intento. La protección supone que los permisos de los roles se verifican en la instalación final y que nadie otorga acceso directo a la función anterior por otros roles. Un servidor en mantenimiento exclusivo es requisito, no una comprobación completa automatizada del prototipo.
