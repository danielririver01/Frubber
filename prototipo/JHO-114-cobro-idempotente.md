# JHO-114 · prototipo aislado de recuperación del cobro

**Estado: experimental, no conectado a Frubber ni apto para clientes.**
[Especificación](../specs/002-cobro-idempotente/spec.md), [plan](../specs/002-cobro-idempotente/plan.md) y [tareas](../specs/002-cobro-idempotente/tasks.md).

## Alcance del prototipo

`database/postgresql/004_CobroIdempotente.sql` crea un registro privado por UUID y la función `app_cobrar_idempotente`. Las solicitudes idénticas del mismo puesto convergen a la misma venta; una solicitud cambiada o de otro puesto se rechaza sin revelar el resultado. La unicidad y la venta están en una misma transacción. La función 003 ya no es ejecutable por el grupo de puestos cuando 004 se aplica, y los puestos no pueden leer ni escribir el registro privado. El objeto JSON de líneas acepta únicamente los seis campos de venta con tipos simples, sin campos adicionales para PIN/credenciales. El JSONB normaliza el orden de propiedades, no el orden de líneas. El prototipo retorna ID, número y total, no un ticket completo.

**Nunca aplicar el prototipo a una base real.** Exige esquema 001–003, instalador dueño de la función 003, ausencia de ventas y mantenimiento exclusivo sin puestos conectados. La comprobación de venta existente no sustituye la exclusividad ni es una migración de datos, ni detecta todos los posibles permisos adicionales. La prueba usa 002, que manipula roles globales y una base de nombre fijo: solo debe ejecutarse en un clúster desechable dedicado, con `FRUBBER_JHO114_ISOLATED=1`, `FRUBBER_JHO114_ADMIN_DSN` y psycopg 3. El banco verifica que no hay bases no predeterminadas ni roles de su lista antes de crear o borrar; la verificación no convierte un servidor compartido en seguro.

## Evidencia 2026-11-06

PostgreSQL 18.6 local, roles reales de prueba, psycopg 3 y READ COMMITTED por defecto; ninguna base del cliente fue usada. Ejecución `prototipo/prueba-cobro-idempotente-aislada.py`:

- Rechazó instalación con venta preexistente, sin alterar dicha venta; negó acceso directo al registro y tablas críticas, y a la función anterior para ambos puestos.
- Una venta y su repetición devolvieron igual ID/número/total sin descontar dos veces. Cambios de solicitud, otro puesto, campos de línea no permitidos y UUID nulo se rechazaron. Se recuperó igualmente tras cambiar precio y cerrar caja; el puesto inactivo fue rechazado.
- Un rollback explícito y otro por fallo inyectado de línea dejaron sin registro ni número consumido. Un competidor con el mismo UUID esperó a que el primero hiciera rollback y completó el número 2.
- Diez conexiones con el mismo UUID devolvieron la misma venta número 3. Dos puestos compitieron por otro UUID: uno obtuvo la venta número 4, el otro fue rechazado.
- Diez conexiones y dos puestos efectuaron 500 ventas distintas, números 5–504 sin huecos. Un proxy TCP dejó pasar el `COMMIT` de la venta 505 y descartó la respuesta real; psycopg observó corte y la repetición obtuvo el mismo resultado. Se comprobaron 505 ventas, líneas y registros completos, contador 505 e inventario coherente. El proxy no usó TLS.
- La base y los roles creados se eliminaron; la comprobación posterior no halló bases ni roles del banco residuales.

Esta evidencia es SQL directo, **no** prueba Npgsql, WPF, PIN autenticado, licencia, TLS, persistencia tras reinicio del cliente, impresión de ticket ni máquinas Windows. JHO-114 y JHO-136 siguen en curso; las pruebas Windows se agrupan al final, pero no se omiten como requisito de cierre/distribución.
