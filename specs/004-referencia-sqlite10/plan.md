# Captura y verificación de la muestra

1. Leer el respaldo con SQLite en modo de solo lectura y mantener una instantánea durante la copia con la API de respaldo.
2. Verificar integridad completa y claves foráneas. Comparar el esquema y cada valor de cada tabla entre origen y copia.
3. Exportar las definiciones de tablas, índices y disparadores, reproducirlas en una base vacía y comprobar la estructura completa.
4. Registrar huellas, recuentos, fecha y procedencia confirmada. Verificar que el origen y su WAL conservan sus huellas.
5. Revisar los resultados contra los criterios de Linear, comprobar restore y build y publicar la evidencia.

El protocolo de pruebas se aplica a los efectos de esta captura documental. Las comprobaciones usan la base real y resultados persistidos. La revisión de aceptación considera copia, esquema, procedencia e integridad. La creación de funcionalidad de migración y sus pruebas de comportamiento corresponden a JHO-124.
