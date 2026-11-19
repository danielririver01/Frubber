# Verificación de la migración JHO-124

- [x] Confirmar la referencia JHO-123 y verificar la FRB1 recuperada con la clave pública de producción.
- [x] Ejecutar las pruebas antes de implementar. Los escenarios de importación correcta y reintento fallaron con el importador pendiente.
- [x] Implementar importación y verificación transaccional de destino.
- [x] Probar destino ocupado, firma inválida, fallo de escritura con reintento y divergencia de inventario que obliga a revertir.
- [x] Ejecutar el comando Release con la copia real en PostgreSQL desechable con TLS y verificar su informe.
- [x] Registrar las condiciones del cambio operativo y el manejo de una caja abierta en el respaldo.
