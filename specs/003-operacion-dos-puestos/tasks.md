# Tareas de integración de dos puestos

- [x] T001 Registrar escenarios y restricciones de JHO-136, ADR-029, ADR-030 y AD-03.
- [x] T002 Implementar la apertura de caja del puesto por función autorizada, con exclusividad por terminal.
- [x] T003 Integrar comprobación del puesto, PIN, apertura y cobro en aplicación y datos con TLS verificado.
- [x] T004 Ejecutar la prueba con dos roles, concurrencia y rollback sobre una base desechable.
- [x] T005 Integrar configuración DPAPI, arranque y licencia 2.0 con la composición WPF.
- [x] T006 Ejecutar build y regresión en Windows. Registrar resultados, alcance comprobado y brechas.
- [x] T007 Verificar en el ejecutable Windows la apertura y el cobro con dos puestos distintos y una licencia FRB2 firmada para dos cajas.
- [x] T008 Repetir la regresión de Windows con el arranque 2.0 y las pruebas de licencia firmada.
- [x] T009 Cerrar la caja propia en 2.0 y conservar el resumen de efectivo, transferencias y ventas de esa sesión.
- [x] T010 Verificar que el otro puesto sigue vendiendo y que repetir el cierre conserva el resumen original.
- [x] T011 Empaquetar la compilación x64 del puesto para la prueba presencial en Windows.
- [x] T012 Instalar en el segundo equipo, provisionar su perfil protegido y comprobar que puede seguir cobrando tras el cierre del otro puesto. Validado en dos PC Windows por Tailscale y aceptado por el dueño como evidencia suficiente el 2026-11-11.

El banco WPF usó la ruta real `--puesto`, dos archivos DPAPI y una licencia firmada con una clave de laboratorio. Los perfiles se ejecutaron por separado en el mismo Windows, con dos cajas abiertas simultáneamente en el servidor. La compilación normal usa la clave pública del dueño.

- [x] T013 Conectar Productos, Usuarios e Historial existentes a la composición PostgreSQL 2.0 de JHO-136, manteniendo sus pantallas y reglas aprobadas. Permitir las operaciones autorizadas por rol, sin conceder a cajeros administración ni acceso directo a datos críticos. Verificar los flujos WPF 2.0 y la matriz SQL.
  - Listado y detalle de Historial conectados a las vistas existentes para la sesión ADMIN. El detalle es de solo lectura y las líneas se consultan por la función acotada de `008_AdministracionPuesto.sql`. El servidor vincula la autorización temporal al puesto y al administrador autenticado por PIN.
  - Productos y Usuarios reutilizan sus pantallas, editores y casos de uso. `009_ProductosUsuariosPuesto.sql` autoriza operaciones por sesión administrativa y aplica el bloqueo transaccional en el servidor. El historial comercial y los registros de PIN impiden eliminar usuarios con actividad.
  - Release de Windows correcto (2 avisos xUnit preexistentes). La integración optativa `TwoWorkstations_OpenSellAndCloseIndependently` pasó sobre un PostgreSQL 18.6 desechable con TLS. Verificó alta, edición, existencias, desactivación y eliminación según historial en Productos, y alta, edición, cambio de PIN, ingreso desde otro puesto, desactivación y eliminación según historial en Usuarios.
  - En WPF se construyeron las pantallas y se cargaron Productos, Usuarios e Historial con datos del servidor, incluido el detalle de solo lectura. Se verificó que el menú administrativo aparece al ingresar como ADMIN y queda oculto para CAJERO. En SQL se rechazaron la escritura directa, el uso de una sesión de otro puesto, el uso sin autorización y la desactivación del último administrador.
  - Regresión final en Windows con `dotnet test FrubberPOS.sln -c Release --no-build --no-restore`: 747 aprobadas, 0 fallos y 2 optativas omitidas (4 min 9 s). La de dos puestos pasó por separado con TLS. El corte de conexión durante COMMIT conserva su evidencia anterior en la especificación 001.
  - Validación automatizada en Windows x64. La instalación de laboratorio anterior requiere aplicar los scripts 008 y 009 y actualizar los binarios para mostrar este menú.

Paquete local de T011: `installer/output/FrubberPOS-Setup-2.0.0-lab.1.exe`. SHA-256: `7E1D1263002B59C406CDC51C9E822FCD2068BDA83EF685B08493D0F9235E67B9`. El asistente existente instala los binarios. La prueba 2.0 requiere el perfil DPAPI creado en la cuenta del puesto y el arranque con `--puesto`. La instalación y la validación en el segundo PC se completaron en T012 por Tailscale.
