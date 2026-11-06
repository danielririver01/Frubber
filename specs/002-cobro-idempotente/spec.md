# Especificación: recuperar un cobro tras un corte de conexión

**Identificador**: `002-cobro-idempotente` (no es rama) | **Estado**: en desarrollo
**Issue**: JHO-114, relacionado con JHO-136 | **Fecha**: 2026-11-06

## Escenarios de usuario y pruebas

### Historia 1 — Recuperar el comprobante (P1)

Un cajero cobra; se interrumpe la conexión antes de recibir la respuesta. Al recuperar la conexión, el sistema reintenta ese mismo cobro y devuelve su comprobante si ya fue registrado, sin volver a descontar inventario ni consumir otro número.

**Prueba independiente**: perder la respuesta después de que el servidor confirme un cobro y volver a enviar el mismo intento; comparar venta, número, inventario y total.

1. **Dado** un cobro confirmado y una respuesta perdida, **cuando** se reintenta el mismo intento desde el mismo puesto, **entonces** se devuelve la misma venta y número.
2. **Dado** un cobro revertido antes de confirmarse, **cuando** se reintenta el mismo intento, **entonces** se registra una sola venta completa.
3. **Dado** dos envíos simultáneos idénticos del mismo intento, **cuando** se completan, **entonces** ambos reciben el mismo resultado y hay una sola venta.

### Historia 2 — Evitar mezclar cobros y puestos (P1)

Un identificador de intento no puede reutilizarse para cambiar el contenido de un cobro ni recuperar datos de otro puesto.

**Prueba independiente**: enviar el mismo identificador con un total o producto diferente, o desde otro puesto, y verificar rechazo sin modificaciones ni divulgación del comprobante original.

1. **Dado** un intento registrado, **cuando** un puesto reutiliza su identificador con datos distintos, **entonces** se rechaza sin nueva venta, número ni cambios de inventario.
2. **Dado** un intento registrado por otro puesto, **cuando** se usa su identificador, **entonces** no se devuelve su venta ni se crea una nueva.
3. **Dado** un puesto inactivo o una operación fallida, **cuando** se intenta cobrar, **entonces** no queda un intento incompleto confirmado ni se consume un número.

### Historia 3 — Preservar la seguridad y las bases existentes (P1)

La protección de reintentos no reemplaza el PIN, la autorización, la licencia o la migración de clientes; la aplicación incompleta no se habilita para producción.

**Prueba independiente**: verificar que el rol de puesto no puede escribir tablas críticas ni saltarse la ruta protegida; verificar que la preparación no modifica bases de clientes.

1. **Dado** un puesto con permisos mínimos, **cuando** intenta escribir directamente en ventas, contador o registros de intento, **entonces** se deniega.
2. **Dado** una base que ya contiene ventas anteriores sin historial de intentos completo, **cuando** se pretenda activar esta versión experimental, **entonces** se rechaza sin modificarla; el traslado de datos se resuelve por separado.

### Casos límite

- El cajero reintenta después de cerrar la caja o cambiar el precio del catálogo: se devuelve el comprobante previo si el intento es idéntico y el puesto sigue activo.
- Un identificador falta, un producto no es válido o falla la inserción de una línea: se revierte toda la operación.
- El cliente se cierra antes de recuperar el resultado: debe conservar el identificador y el contenido del intento hasta resolverlo; esto exige trabajo de aplicación y almacenamiento seguro adicional.
- Si el intento se cambia, se usa desde otro puesto, o se desconoce su resultado, no se inventa un comprobante ni se crea otra venta con otro identificador.

## Requisitos

- **RF-001**: El puesto genera un identificador único antes del cobro y conserva el intento hasta resolver su resultado.
- **RF-002**: El identificador, datos del intento, venta, número, líneas e inventario se confirman o revierten juntos.
- **RF-003**: Un reintento idéntico desde el puesto original devuelve la misma venta sin volver a consumir número ni inventario.
- **RF-004**: Los envíos simultáneos con el mismo identificador producen como máximo una venta.
- **RF-005**: Un identificador repetido con datos distintos o desde otro puesto se rechaza sin divulgar la venta ni modificar inventario.
- **RF-006**: No se otorga acceso directo de escritura o lectura a las tablas críticas para puestos; el puesto no puede saltarse el control de reintentos.
- **RF-007**: Las bases de clientes y ventas existentes no se modifican ni migran como efecto de las pruebas o de este experimento.
- **RF-008**: La validación del PIN y la licencia, el almacenamiento del intento en el cliente y las pruebas reales de la aplicación siguen siendo requisitos antes de activar el flujo.

### Entidades principales

- **Intento de cobro**: identificador único, puesto y contenido que no deben cambiar durante el reintento.
- **Venta confirmada**: comprobante y efectos que se recuperan exactamente una vez.
- **Puesto**: identidad de la conexión de caja, no elegida por un parámetro enviado por el cliente.

## Criterios de éxito

- **CE-001**: En un corte después de confirmar y una repetición idéntica hay una venta, un solo número e inventario descontado una vez, y el resultado coincide.
- **CE-002**: Dos conexiones simultáneas con el mismo intento obtienen la misma venta; un intento con datos cambiados u otro puesto se rechaza sin efectos.
- **CE-003**: El fallo antes de confirmar deja cero ventas parciales y el siguiente cobro usa el número sin hueco.
- **CE-004**: Se ejecutan pruebas de permisos, flujo de aplicación con recuperación de intento y compilación/regresión en Windows antes de cerrar JHO-114.

## Suposiciones y límites

- La primera versión del mecanismo es experimental y se ensaya solo en un servidor desechable; no se ejecuta contra instalaciones reales.
- El cliente debe guardar el intento de manera durable antes de enviarlo. Un UUID único por sí solo no garantiza recuperar un intento que el cliente perdió.
- La función existente comprueba que el cajero esté activo, pero no enlaza su PIN con la sesión de servidor. No se afirma autorización completa ni se habilita distribución por estas pruebas.
