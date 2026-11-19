# Migrar un respaldo de la versión 1.0

Ejecuta esta herramienta en el equipo que será el servidor. Prepara una copia SQLite consolidada con las cuatro migraciones de la 1.0 y una base PostgreSQL nueva dedicada. Usa la cuenta administradora del motor, TLS VerifyFull y el certificado público del servidor. Cierra la caja en la 1.0 antes de generar el respaldo que usarás en el cambio operativo.

```powershell
dotnet run --project tools/FrubberPOS.DataMigration -c Release -- <respaldo.db> <licencia.frb1> <informe.json>
```

La herramienta solicita la conexión del administrador con entrada oculta en la consola. Admite una línea por entrada estándar para automatización local. Indica un archivo nuevo para el informe.

Si el respaldo tiene licencia activada, entrega el texto FRB1 completo y firmado para el equipo servidor. Si el respaldo carece de licencia activada, indica `-` en el argumento de licencia. La 1.0 guardaba con DPAPI el contenido canónico de la licencia. Para la muestra se recuperó la FRB1 firmada con el mismo contenido, con autorización expresa del dueño. El importador usa la clave pública y compara el contenido con el sello cuando la cuenta Windows puede abrirlo. Si el sello pertenece a otro equipo o cuenta, el informe indica que esa comparación quedó pendiente.

La herramienta aplica o verifica la línea base y luego importa en una sola transacción. Si el cuadre falla, se revierten los datos de la importación. La estructura inicial puede quedar lista para otro intento. Si se pierde la confirmación del motor, comprueba el destino antes de reintentar. El informe indica el número de filas comparadas por tabla, el resultado del cuadre y la verificación de la licencia.

El historial de caja queda en «Puesto desconocido (versión 1.0)». Un respaldo con una caja abierta puede verificarse en laboratorio y el informe conserva ese estado. Antes de continuar con la operación en la 2.0, cierra esa caja en la 1.0 y genera el respaldo de corte. La configuración de la báscula, los sellos temporales de prueba y los desfases horarios originales se conservan en el destino como datos legados. El aprovisionamiento y las reglas operativas de la 2.0 se gestionan por sus flujos correspondientes.
