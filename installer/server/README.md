# Frubber POS 2.0 · Caja principal

Este paquete de laboratorio contiene PostgreSQL 18.7 x64, el redistribuible de Visual C++ y los instaladores de Frubber. Se puede llevar por USB al Windows de prueba.

1. Extraiga el ZIP completo en un Windows x64 limpio. Reserve una IPv4 en la red local del negocio y configure esa red como privada o de dominio. La instalación utiliza el puerto 5432.
2. Abra Windows PowerShell de 64 bits como administrador **con la cuenta que usará la caja principal**. Desde la carpeta extraída, ejecute el instalador. Ingrese la IPv4 local, guarde la contraseña de PostgreSQL y elija el PIN de administrador de Frubber.

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Instalar-CajaPrincipal.ps1
   ```

3. Complete el asistente de Frubber y abra el acceso directo. Seleccione **Administrador** e ingrese el PIN elegido.
4. Reinicie el servidor. Compruebe que el servicio **FrubberPOSPostgreSQL** queda iniciado automáticamente y que Caja principal vuelve a abrir. Desde el segundo PC, aprovisione un puesto con la herramienta de administración del servidor, el certificado público **Servidor-Frubber.pem** y un perfil protegido para su cuenta Windows. Compruebe que llega al inicio de sesión del puesto. Registre la fecha y los resultados en E-02, E-03, E-04 y E-06.

El asistente prepara el motor, el esquema, el certificado TLS, la primera caja y una tarea de respaldo diario a las 2:00 de la madrugada. Los datos y respaldos se guardan bajo `C:\ProgramData\FrubberPOS-Servidor`. Comparta únicamente el certificado público con otros puestos. La clave privada queda en la carpeta de datos del servidor.

Si el motor ya quedó preparado y cerró el asistente de Frubber, puede volver a abrir el asistente con los archivos generados desde la misma cuenta Windows.

```powershell
.\FrubberPOS-Setup.exe --certificado-puesto "C:\ProgramData\FrubberPOS-Servidor\Servidor-Frubber.pem" --perfil-puesto "C:\ProgramData\FrubberPOS-Servidor\private\CajaPrincipal.cfg"
```

Si falla la preparación del servidor, guarde el mensaje y el registro en `data\log`. El asistente conserva los datos y los archivos de diagnóstico. Use un Windows limpio o pida a soporte revisar la instalación parcial antes de reintentar.
