# Informe de migración JHO-124

Prueba del 19 de noviembre de 2026 con el comando Release, una copia verificada de un respaldo real de Frubber POS 1.0.0 y un PostgreSQL desechable por TLS VerifyFull en el equipo de desarrollo.

| Datos comprobados en el destino | Resultado |
| --- | --- |
| Usuarios y estado de actividad | Coinciden |
| Productos, precios y existencias | Coinciden |
| Cajas, saldos y puesto histórico | Coinciden |
| Ventas, consecutivo, líneas y anulaciones | Coinciden |
| Configuración, báscula y estado de prueba | Coinciden |
| Licencia FRB1 | Firma de producción y contenido del sello comprobados |

El importador recalculó los saldos de las cajas cerradas y los subtotales a partir de las ventas y sus líneas. Comparó cada valor migrado con el destino antes de confirmar. La muestra contenía una caja abierta, conservada bajo el puesto histórico. El informe del comando marcó todas las comprobaciones como coincidentes.

Cuatro pruebas de integración aprobaron en el clúster aislado. Se comprobó que un error de escritura revierte la importación y permite reintentar. Un disparador de prueba alteró una existencia sin lanzar error y el cuadre detectó la diferencia, revirtió la operación y dejó vacíos los datos que se habían escrito. También se verificaron el rechazo de una firma inválida y la preservación de un destino ocupado.

El respaldo con datos, la licencia y el informe con los detalles de la muestra se conservan localmente. Se eliminó el clúster temporal al finalizar.
