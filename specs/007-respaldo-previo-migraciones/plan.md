# Verificación del respaldo previo

El arranque del servidor conecta el ejecutor de migraciones con pg_dump. El archivo se escribe provisionalmente y se publica en Backups/Premigracion tras comprobar la salida de pg_dump y el encabezado y cierre del volcado. El acceso usa los parámetros de servidor y TLS de la conexión configurada. El bloqueo de migraciones abarca la lectura del historial, la copia y la aplicación de los scripts.

La prueba de integración usa dos bases de un clúster temporal. Inserta un valor antes de migrar, aplica un script que lo cambia y restaura la copia en otra base para comparar el estado anterior. Otra prueba usa un certificado raíz inexistente para provocar un fallo de pg_dump y comprueba datos, esquema, historial y ausencia de archivos parciales. La revisión de alcance incluye el catálogo de arranque existente. El inicializador 2.0 prepara bases nuevas y los perfiles de puesto operan sobre bases preparadas.
