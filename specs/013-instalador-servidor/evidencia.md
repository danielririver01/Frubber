# Evidencia de JHO-121

El banco local usa los mismos binarios de PostgreSQL 18.7 y el mismo código que instala la base. Se inicia un clúster temporal por loopback en un puerto libre distinto de 5432. La prueba verifica la línea base, el puesto inicial, el PIN, el perfil protegido, TLS VerifyFull, el rechazo de una conexión sin TLS y la generación de los dos archivos de respaldo. El respaldo se abre con `pg_restore --list`.

El 19 de noviembre de 2026 pasó el banco `tests/Test-ServerPackage.ps1`. También comprobó el tratamiento Unicode de una mayúscula con tilde y que se retire el archivo temporal de inicialización. El build Release de la solución pasó con dos avisos xUnit preexistentes. El lanzador rechazó la ejecución sin administrador antes de preparar recursos.

El archivo `installer/output/FrubberPOS-CajaPrincipal-2.0.0-lab.4.zip` contiene 570.391.690 bytes. SHA-256 `1779E8E39E71B16E5ECBFDC39FB5E1111030DE72CCB967F6ACAE6343B8D0A37B`. La validación del ZIP comprobó CRC, sus diez entradas, los hashes de EDB y Visual C++ y la coincidencia de los guiones con el código fuente.

El 20 de noviembre de 2026 se instaló lab.6 en Windows 11 dentro de VirtualBox, con red de laboratorio y cero rutas predeterminadas IPv4 e IPv6. El registro confirmó la creación de la base y del perfil protegido. Frubber abrió la pantalla de inicio de sesión de Caja principal con el usuario Administrador leído desde PostgreSQL. SHA-256 de lab.6: `A1C4E3BC26924C998559B6D8738C6314E76C51792F0DA74665763E7F38547D70`.

Tras reiniciar Windows a las 11:58:48, el servicio PostgreSQL quedó activo con inicio automático y escuchó en loopback y en la IP de laboratorio. Se abrió Frubber desde el acceso directo de la VM y la Caja principal volvió a mostrar el usuario Administrador. El anfitrión no recibió respuesta por la red de laboratorio. Windows clasificó la interfaz aislada como pública tras reiniciar y la regla de Frubber está habilitada para redes privadas y de dominio.

Tras volver a clasificar la interfaz aislada de la VM como privada, PostgreSQL respondió desde el PC anfitrión. Se inició la tarea programada de respaldo bajo demanda. Windows la ejecutó como SYSTEM y devolvió código 0. Se verificaron un archivo nuevo de base de datos y su archivo de roles. `pg_restore --list` pudo leer el catálogo del respaldo.

Las cinco pruebas `EngineDownTests` pasaron en Release. Con la conexión rechazada, el TPV informa al cajero, bloquea el cobro y conserva el borrador de venta sin registrarlo.

El PC físico anfitrión se autenticó con el rol del segundo puesto en la base `frubber` mediante TLS VerifyFull por la red aislada de VirtualBox. La consulta confirmó cliente `192.168.56.1`, servidor `192.168.56.10` y TLS activo. Frubber lab.6 abrió la pantalla de inicio de sesión titulada «Caja secundaria laboratorio». Se registraron E-02, E-03, E-04 y E-06 en la bitácora 6.2.
