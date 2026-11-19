# Evidencia de JHO-121

El banco local usa los mismos binarios de PostgreSQL 18.7 y el mismo código que instala la base. Se inicia un clúster temporal por loopback en un puerto libre distinto de 5432. La prueba verifica la línea base, el puesto inicial, el PIN, el perfil protegido, TLS VerifyFull, el rechazo de una conexión sin TLS y la generación de los dos archivos de respaldo. El respaldo se abre con `pg_restore --list`.

El 19 de noviembre de 2026 pasó el banco `tests/Test-ServerPackage.ps1`. También comprobó el tratamiento Unicode de una mayúscula con tilde y que se retire el archivo temporal de inicialización. El build Release de la solución pasó con dos avisos xUnit preexistentes. El lanzador rechazó la ejecución sin administrador antes de preparar recursos.

El archivo `installer/output/FrubberPOS-CajaPrincipal-2.0.0-lab.4.zip` contiene 570.391.690 bytes. SHA-256 `1779E8E39E71B16E5ECBFDC39FB5E1111030DE72CCB967F6ACAE6343B8D0A37B`. La validación del ZIP comprobó CRC, sus diez entradas, los hashes de EDB y Visual C++ y la coincidencia de los guiones con el código fuente.

Pendiente de la jornada en Windows limpio. Instalar con internet desconectado, reiniciar y verificar arranque automático, iniciar Caja principal y conectar desde otro PC en la subred. Disparar la tarea programada y verificar los archivos generados bajo la identidad del servicio programado. Registrar los resultados en E-02, E-03, E-04 y E-06 de la bitácora 6.2.
