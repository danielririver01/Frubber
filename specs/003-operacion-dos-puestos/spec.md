# Arranque y operación de dos puestos sobre PostgreSQL 2.0

JHO-136 · 2026-11-10 · Rama `arena/04894ea8-frubber`

## Historias y aceptación

1. Un puesto autorizado inicia Frubber sobre una base 2.0 recién preparada por el servidor. Se identifica con su propio rol por TLS verificado, llega al acceso por PIN y no ejecuta migraciones. Un puesto inactivo o un servidor sin identidad verificable impiden iniciar.
2. Cada cajero se autentica por PIN y abre su propia caja. Una segunda apertura simultánea en el mismo puesto deja una sola caja. Dos puestos pueden tener caja abierta al mismo tiempo.
3. Dos puestos cobran concurrentemente con inventario compartido. Cada venta queda en su caja. Un fallo posterior a la asignación del número revierte venta, líneas, existencias y contador. El siguiente cobro usa el número revertido.
4. El rol del puesto solo tiene las lecturas y funciones previstas por AD-03. La prueba verifica el rechazo de escrituras directas y del cobro cruzado.
5. Cada puesto confirma el cierre de su caja y recibe el resumen de esa sesión. El efectivo esperado suma la base inicial y las ventas en efectivo. Las transferencias se muestran aparte. El otro puesto puede seguir cobrando y repetir un cierre devuelve el resumen original.
6. La composición 2.0 ofrece los flujos de productos, usuarios e historial previstos por JHO-136. La verificación distingue las pantallas y operaciones de la aplicación de las consultas de solo lectura que el motor ya permite al rol de puesto.

## Condiciones

- Base aprobada `001_Baseline.sql`, instalada desde el servidor. No convertir bases anteriores.
- Cifrado con `VerifyFull` y verificación del certificado. Validación de PIN en el motor y comprobación de licencia antes de operar. Los secretos de la prueba se generan en memoria en un clúster desechable.
- La compatibilidad de licencias firmadas y cupos se sigue en JHO-116. La recuperación idempotente del cobro se sigue en JHO-114. Ambas conservan sus condiciones de entrega. La prueba aislada puede usar el puerto de licencia existente con un doble de prueba para separar la evidencia del flujo y la validación de licencia.
- Evidencias distintas para PostgreSQL, capas de aplicación y ejecutable WPF. Para cumplir la primera historia, el ejecutable debe abrir su interfaz sobre el esquema nuevo con la composición y la licencia 2.0 correspondientes.
