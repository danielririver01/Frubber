# Autorizar puestos en el servidor · JHO-116

Ejecutar en el equipo servidor, con .NET 10 y la cuenta de Windows que operará su puesto local. La base 2.0 debe tener la línea base, `002_Security.sql` y la licencia firmada registrada en `license_state`. La aplicación verifica la firma con la clave pública habitual y compara el código firmado con el hardware del servidor.

```powershell
dotnet run --project tools/FrubberPOS.ServerAdministration -c Release -- "Caja 1" "ABCDEF1234567890" "C:\ConfiguracionFrubber\puesto.cfg"
```

La herramienta solicita de forma oculta la cadena de conexión del administrador del motor. Usar `Host=127.0.0.1`, `localhost` o `::1`, `SSL Mode=VerifyFull` y el certificado público de confianza mediante `Root Certificate`. El certificado debe incluir el nombre o IP elegido. La entrada estándar redirigida también se admite. Evitar guardar la cadena en scripts, historial o registros.

El alta bloquea las tablas de licencia y puestos hasta confirmar la operación. Comprueba la licencia, cuenta los puestos activos y llama a `register_terminal` dentro de esa misma transacción. FRB1 autoriza uno. FRB2 usa su cupo firmado. Con cupo lleno muestra, por ejemplo, «El negocio ya tiene 2 de 2 puestos autorizados. La licencia no permite autorizar otro puesto». Al autorizar muestra los puestos ocupados y el cupo total.

El perfil resultante usa DPAPI CurrentUser y se abre con `FrubberPOS.Desktop.exe --puesto <archivo.cfg>` desde la misma cuenta Windows y equipo. El archivo de destino debe ser nuevo y su carpeta debe existir. La incorporación de otros equipos y la administración en pantalla corresponden a JHO-128. Sus perfiles se aprovisionan en la cuenta Windows de destino.

Si una caída impide confirmar el alta o guardar el perfil, consultar el puesto y su rol desde el servidor antes de repetir. Un rol creado cuyo perfil se perdió exige recuperar su aprovisionamiento con el administrador del motor. La herramienta elimina su archivo incompleto y conserva los perfiles anteriores.

La cuenta administradora del motor es la frontera de confianza aprobada en `002_Matriz-de-Permisos.md`. Los puestos mantienen los permisos mínimos existentes. El SQL administrativo directo conserva su acceso privilegiado. El administrador debe utilizar esta herramienta para aplicar el cupo durante el alta.

## Verificación

`Postgres20WorkstationRegistrationTests` comprueba FRB1, ampliación a FRB2, licencia ausente, alterada, futura o de otro servidor, duplicados, cancelación, dos altas concurrentes por la última plaza y liberación de cupo tras desactivar. Se ejecuta exclusivamente con un clúster PostgreSQL desechable por TLS y `FRUBBER_JHO136_ISOLATED=1`, con su conexión en `FRUBBER_JHO116_ADMIN`. Los roles de ese clúster deben estar vacíos al comenzar.
