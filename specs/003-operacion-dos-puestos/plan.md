# Plan de integración de dos puestos

1. Conservar el instalador del servidor y la ruta anterior. Usar los procedimientos de roles, PIN, catálogo y venta aprobados. Agregar una función acotada de apertura de caja que derive la identidad del puesto de la conexión.
2. Integrar la comprobación de puesto, autenticación, apertura y cobro en las capas de aplicación y datos. Reutilizar los contratos de venta, la sesión local, el control de licencia y el esquema existentes. Exigir TLS verificado y mantener las operaciones limitadas a la caja propia.
3. Probar el flujo con PostgreSQL 18.6 desechable, una base instalada por el servidor y dos sesiones reales. Verificar la matriz de permisos, concurrencia, rollback y consecutivos.
4. Conectar el inicio WPF del puesto a un archivo DPAPI de ámbito CurrentUser con la conexión TLS y el código del servidor registrados durante la provisión. Verificar la licencia firmada de PostgreSQL antes de iniciar, abrir caja y cobrar. Una FRB1 habilita una caja y una FRB2 respeta su cupo firmado.
5. Comprobar el acceso, la caja y el TPV del puesto en Windows contra PostgreSQL desechable y con una licencia firmada por una clave de laboratorio. La prueba usa un ejecutable de laboratorio con esa clave pública y el mismo código de arranque. La compilación normal conserva la clave pública del dueño.
6. Cerrar por identificador de sesión de caja, validando el puesto en PostgreSQL. Guardar el resumen en la misma transacción y mostrarlo al volver a la pantalla de apertura. Probar que la otra caja cobra durante y después del cierre, y que repetir el cierre conserva el resumen.
7. Completar los flujos de productos, usuarios e historial de JHO-136 en la composición 2.0 y comprobarlos con los permisos aprobados de cada rol. Conservar como evidencia separada las consultas de catálogo e historial que ya pasaron con un rol de puesto.

La escritura de caja y venta sigue el límite de confianza aprobado por AD-03. Las funciones validan puesto y usuario activo. La aplicación obtiene la identidad del cajero de su sesión autenticada por PIN. El esquema 2.0 y las rutas de prototipo se prueban en un clúster dedicado y desechable.
