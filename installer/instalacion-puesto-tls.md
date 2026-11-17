# Instalar un puesto con certificado verificado

JHO-129. El servidor debe tener TLS habilitado, un certificado que incluya su nombre o dirección y las reglas de conexión del motor configuradas para exigir TLS. El puesto debe estar autorizado previamente.

1. Aprovisionar el perfil DPAPI del puesto bajo la misma cuenta Windows que ejecutará Frubber. La conexión contiene el rol individual y `SSL Mode=VerifyFull`. El certificado público del servidor se entrega como archivo PEM. La clave privada permanece en el servidor.
2. Ejecutar el instalador con ambos archivos. Windows solicita permisos de administrador para instalar la aplicación y registrar sus accesos directos.

   ```powershell
   .\FrubberPOS-Setup.exe --certificado-puesto "C:\Preparacion\servidor.pem" --perfil-puesto "C:\Preparacion\puesto.cfg"
   ```

3. Elegir la carpeta de instalación. El asistente copia el certificado público a la subcarpeta `connection`, comprueba una conexión real con el certificado y guarda un perfil DPAPI con la ruta instalada. La identidad de Windows que eleva el asistente debe poder descifrar el perfil original. Una elevación con una cuenta distinta exige preparar el perfil para la cuenta correspondiente antes de continuar.
4. Al finalizar, abrir Frubber desde el asistente o desde el acceso directo creado. Ambos usan el perfil instalado con `--puesto`. El archivo de preparación y su ruta anterior del certificado pueden retirarse una vez verificado el arranque.

El certificado instalado tiene un nombre derivado de su huella SHA-256. El perfil se reemplaza de forma atómica después de comprobar la conexión. Si falla el certificado o el perfil, el asistente informa del problema y conserva el perfil instalado anteriormente. Una actualización que conserve `connection/puesto.cfg` mantiene ese perfil en los accesos directos.

Para comprobar la instalación, verificar que el acceso directo incluye el perfil de `connection`, que ese perfil contiene `VerifyFull` y la ruta del certificado instalado, y que Frubber llega al acceso por PIN con el servidor disponible. Registrar el resultado en la bitácora 6.2.

## Evidencia disponible del 17 de noviembre de 2026

La prueba .NET Release `WorkstationCertificateInstallerTests` pasó en PostgreSQL temporal con TLS. Comprobó la copia del certificado, la carga del perfil DPAPI instalado y una conexión real que devolvió `SHOW ssl = on`. Verificó el rechazo de un certificado ajeno, un archivo con clave privada, un perfil ilegible y el reemplazo con otro servidor. Comprobó que los rechazos preservan el perfil previo y que repetir una instalación válida funciona.

La regresión final terminó con 752 pruebas aprobadas, cero fallidas y cinco omitidas por condiciones de laboratorio. Las pruebas dirigidas de cupo y certificado se ejecutaron por separado y pasaron. La solución y el instalador compilaron en Release. La solución conserva dos advertencias xUnit2013 previas en `ProductsViewModelTests`.

La prueba completa del asistente elevado sigue pendiente. La revisión automática rechazó abrirlo porque su ejecución puede modificar el registro HKLM y los accesos directos de la instalación existente. El paquete de laboratorio está preparado en `artifacts/jho129-installer`. Los perfiles y el servidor temporales se eliminan al cerrar el banco de pruebas.

El SHA-256 del paquete preparado es `901AEA21C89F541DD753E3D453ADBA61F615FB00D20FFE83FC8847018F88F3DC`.
