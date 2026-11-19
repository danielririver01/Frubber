# Plan de migración

1. Abrir el respaldo consolidado en modo de solo lectura y comparar su esquema con la referencia JHO-123. Verificar integridad, claves foráneas y cuadre de origen.
2. Usar la línea base existente y preparar el estado legado dentro de la transacción de importación. Serializar importaciones y rechazar destinos ocupados.
3. Cargar el historial en orden de dependencia con los disparadores activos. Comparar cada valor migrado y preparar las secuencias antes de confirmar.
4. Ejecutar primero los escenarios de aceptación contra PostgreSQL aislado. Verificar también una desviación persistida que no produce errores de escritura.
5. Ejecutar el comando Release con la muestra real y la FRB1 recuperada. Registrar resultados y condiciones del cambio operativo.
