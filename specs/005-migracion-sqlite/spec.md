# Migración verificada de SQLite a PostgreSQL

JHO-124. Importar el respaldo de la versión 1.0 en un destino dedicado con la línea base PostgreSQL 2.0. Conservar productos, cantidades, ventas, anulaciones, cajas, usuarios, licencia y configuración persistida.

1. Un respaldo real se importa con sus valores y referencias. La herramienta comprueba el cuadre de las ventas, saldos de caja e inventario y guarda un informe.
2. Una escritura fallida o una divergencia al consultar el destino revierte todo el lote de datos. Un destino ocupado se rechaza.
3. La prueba se hace fuera del entorno operativo del negocio.

Las cajas de la 1.0 se asignan al puesto histórico. La licencia FRB1 completa corresponde al servidor de destino y se verifica con la clave pública de producción. El respaldo histórico conserva el sello SQLite y la herramienta compara su contenido si Windows permite abrirlo.
