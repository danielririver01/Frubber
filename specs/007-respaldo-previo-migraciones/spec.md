# Respaldo previo a migraciones

JHO-120 aplica ADR-008 y ADR-018 al arranque que ejecuta el catálogo de migraciones en PostgreSQL.

- Cuando hay scripts pendientes y el historial es válido, el servidor genera y verifica un respaldo antes de ejecutar el primer script.
- Si el respaldo falla, el arranque informa el problema y deja intactos los datos y el historial de migraciones pendientes.
- Cuando el catálogo ya está aplicado, el servidor inicia sin crear otro respaldo. Dos arranques que revisan la misma base coordinan la copia y la aplicación.
- El arranque con perfil de puesto conecta a la base preparada. El catálogo de arranque no se ejecuta en esa ruta.
