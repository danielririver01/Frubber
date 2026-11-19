# Tareas de JHO-120

- [x] Revisar el catálogo de arranque y el comportamiento de respaldo de PostgreSQL.
- [x] Generar el respaldo antes de aplicar scripts pendientes y bloquear la migración si falla.
- [x] Comprobar la restauración del estado anterior y que el respaldo se genere una sola vez con arranques concurrentes.
- [x] Añadir la comprobación de fallo real de pg_dump con el estado de origen intacto.
- [x] Verificar restore y build Release y registrar la ejecución en PostgreSQL TLS desechable. Pasaron 45 pruebas de respaldo, migraciones y esquema. La regresión incluye la pérdida de confirmación. El build mostró dos advertencias xUnit2013 preexistentes en ProductsViewModelTests.
