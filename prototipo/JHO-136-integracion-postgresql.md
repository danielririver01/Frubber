# JHO-136 · Integración progresiva de la línea base 2.0

## Estado

La aplicación ofrece una ruta de puesto 2.0 para iniciar sesión, abrir caja y cobrar sobre la línea base nueva. Su verificación es de laboratorio. La migración de bases existentes y los demás flujos y candados del piloto se siguen en sus tareas.

Se agregó un instalador de la línea base 2.0, aislado de la composición de la aplicación y destinado al servidor. No se ejecuta al abrir Frubber. Su alcance es exclusivamente `001_Baseline.sql`, embebido directamente desde `database/postgresql/` para no mantener dos copias. No instala seguridad, no da de alta puestos ni migra datos. En una base nueva y dedicada crea el esquema y registra nombre y checksum en la misma transacción. Serializa ejecutores que usen ese mismo instalador; el administrador debe garantizar que ninguna otra herramienta modifique la base durante la instalación. Rechaza bases no vacías, historial desconocido o modificado y mezclas con el historial anterior. Un registro válido existente devuelve cero sin reaplicar el guion; **esto no verifica que el resto del esquema no haya sido modificado**.

Como protección temporal, el migrador usado por la aplicación anterior rechaza la presencia de `public.schema_migration`, incluso si está vacío o coexiste con el registro anterior. Esto evita intentar aplicar las migraciones anteriores sobre una base 2.0; no convierte la aplicación en compatible con ella. Ambos ejecutores están diseñados para `public`; no utilizar esquemas alternativos para instalar Frubber.

## Seguridad antes de conectar el instalador

`002_Security.sql` **no se aplica desde este instalador**. Actualmente contiene referencias literales a la base `frubber` y al rol `postgres`, crea o reutiliza roles de clúster y puede eliminar el rol de prototipo `frubber_puesto_test`. Por eso no debe ejecutarse automáticamente ni en un servidor compartido sin revisar y corregir esas operaciones globales y probar el aprovisionamiento. La protección DPAPI, el transporte seguro, los permisos y las operaciones autorizadas siguen siendo requisitos de distribución.

`003_RegistroVenta.sql` aún no vincula el usuario de caja a una autenticación de PIN en el servidor y rechaza solicitudes repetidas sin devolver la venta previa. La recuperación idempotente corresponde a JHO-114. Siguen pendientes el arranque 2.0 y los flujos de caja por puesto, productos, usuarios, historial y licencia sobre la línea base aprobada. La migración de clientes tiene su propio issue, JHO-124.

## Pruebas

`Postgres20BaselineInstallerTests` crea únicamente bases temporales aleatorias mediante `PostgresTestDb.CreateScratchAsync`; requiere un PostgreSQL de pruebas y un usuario con permiso CREATEDB. No usar una base o servidor de producción. La clase cubre instalación/repetición, rechazo cruzado de migradores, objetos y datos preexistentes, checksum e historial desconocido, historial mezclado y dos instaladores concurrentes. En Windows:

```powershell
# Configurar FRUBBERPOS_TEST_CONNECTION_STRING para un clúster desechable en 127.0.0.1.
$env:FRUBBER_JHO136_ISOLATED = '1'
dotnet restore FrubberPOS.sln
dotnet build FrubberPOS.sln -c Release --no-restore
dotnet test tests/FrubberPOS.Tests/FrubberPOS.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Infrastructure.Migrations"
```

### Comprobación SQL aislada · 2026-11-05

En PostgreSQL 18.6 local, en un clúster desechable, se ejecutaron las mismas consultas y línea base siguiendo el protocolo de transacciones del instalador mediante Python/psycopg: dos conexiones concurrentes serializadas obtuvieron resultados 1 y 0; una versión futura fue rechazada; una tabla con datos y un esquema ajeno se conservaron intactos; un fallo inyectado después del guion revirtió la línea base. Las cuatro bases temporales se eliminaron. Esto verifica el protocolo SQL, **no ejecuta el código C# ni las pruebas xUnit**. En este entorno no hay SDK .NET ni Windows/WPF; falta compilar y ejecutar las pruebas y la suite de regresión en Windows.

### Refuerzo de precondiciones · 2026-11-06

Al aplicar Spec Kit al tramo de inicialización se detectó que una base que solo contenía una `collation` en `public` pasaba la comprobación inicial de objetos. El instalador ahora incluye dependencias de objetos de `public` y varias clases de objetos propios de la base (publicaciones, suscripciones, adaptadores/servidores externos, disparadores de evento, objetos grandes y lenguajes adicionales). Se agregaron casos xUnit para collation, publicación y objeto grande; **aún no se ejecutaron en .NET**.

Se agregó `prototipo/prueba-inicializador-jho136.py`. En PostgreSQL 18.6 aislado, su ejecución emuló el protocolo SQL del instalador, verificó instalaciones concurrentes, historial futuro, rollback y rechazo/conservación de seis tipos de objetos existentes (incluida una collation que la consulta anterior no detectaba); las ocho bases de nombres aleatorios fueron eliminadas. El banco completo se repitió con un rol de prueba sin superusuario y con CREATEDB; pasó y se eliminaron las ocho bases y el rol temporal. Esta evidencia **no ejecuta el instalador C# ni demuestra que el catálogo detecte absolutamente todos los objetos posibles**. Solo se debe utilizar un destino nuevo y dedicado bajo exclusividad operativa.

Reproducción en un clúster de pruebas desechable y dedicado (no producción):

```powershell
python -m pip install "psycopg[binary]>=3.2,<4"
$env:FRUBBER_JHO136_ISOLATED = '1'
$env:FRUBBER_JHO136_ADMIN_DSN = 'host=127.0.0.1 port=5432 dbname=postgres user=usuario_de_prueba'
python prototipo/prueba-inicializador-jho136.py
if ($LASTEXITCODE -ne 0) { throw 'Falló la comprobación aislada JHO-136' }
```

La señal `FRUBBER_JHO136_ISOLATED=1` es una afirmación del operador, no verifica que el servidor sea realmente desechable. El usuario requiere CREATEDB y el banco usa `DROP DATABASE ... WITH (FORCE)` solo sobre los nombres aleatorios que él mismo creó; si el proceso se interrumpe abruptamente podría quedar alguna base temporal. No se guardan credenciales en el repositorio.

### Implementación de fallos y confirmación incierta · 2026-11-06

El instalador aislado ahora distingue la cancelación previa a intentar confirmar de una cancelación, error de E/S o tiempo de espera durante la confirmación: en este último caso devuelve un resultado de almacenamiento incierto y pide revisar el historial, sin afirmar que la instalación se revirtió. Se añadieron puntos internos de inyección usados solo por las pruebas de integración: uno induce un fallo SQL después de aplicar el esquema y antes de registrarlo; otro simula una respuesta perdida después de una confirmación real. La construcción normal no inyecta ninguno de estos comportamientos.

Se escribieron pruebas xUnit para comprobar rollback y posterior reintento, y para simular error de E/S o cancelación después de que el servidor ya confirmó, revisar que existe el registro y reintentar sin duplicarlo. **No se han ejecutado ni compilado estas pruebas C#**. La simulación de xUnit no es un corte de red real.

Además, se ejecutó por separado una comprobación real en PostgreSQL 18.6 aislado mediante psycopg y un proxy TCP local: el proxy cortó la conexión al enviar `COMMIT`, el cliente recibió `OperationalError` y una conexión independiente comprobó que esquema y registro ya habían quedado confirmados. La base temporal aleatoria se eliminó. Esto demuestra el caso ambiguo en el protocolo PostgreSQL, no el comportamiento de Npgsql ni de la aplicación; una prueba real de corte de red con Npgsql y la verificación de posibles errores durante la liberación de la conexión continúan pendientes.

El ensayo de corte es reproducible con `prototipo/prueba-corte-commit-jho136.py`, utilizando las mismas variables del banco anterior y un PostgreSQL **local, desechable y dedicado**. El proxy de prueba solo escucha en loopback y desactiva TLS para observar el intercambio: no usarlo con contraseñas o datos de producción. Con el indicador de aislamiento ausente, el script se negó a ejecutar (salida 1). Con el indicador y el clúster de prueba, pasó y eliminó su única base temporal. Si la ejecución se interrumpe abruptamente podría quedar una base temporal que un operador debe revisar.

### Protección frente a objetos que imitan funciones internas · 2026-11-06

Se comprobó en una base temporal que la ruta anterior `public, pg_catalog` podía resolver una función de `public` llamada `to_regclass` antes que la del sistema. Aunque el instalador no está conectado al arranque, esto era peligroso para una base no confiable. El instalador ahora califica las funciones y catálogos del sistema y usa `public, pg_temp` como ruta explícita, dejando que PostgreSQL busque `pg_catalog` primero de forma implícita; `public` sigue siendo el esquema de creación. El candado del migrador anterior también califica su consulta. Además, antes de leer un historial existente, el instalador rechaza vistas, tablas no permanentes y tablas con seguridad por fila, para no ejecutar una consulta o política ajena durante la comprobación. Estas barreras no certifican la integridad completa de una base ya existente.

En PostgreSQL 18.6 aislado se demostró primero el fallo de resolución anterior con una función sintética que lanza un error; luego el banco `prueba-inicializador-jho136.py` confirmó rechazo sin ejecutarla, incluso con una ruta de búsqueda inicial no confiable. También probó funciones que imitan el bloqueo, una vista de historial con una función trampa, una tabla de historial con seguridad por fila y el candado anterior. Se repitió el banco completo con un rol sin superusuario y CREATEDB: **12 bases temporales eliminadas**, cero residuales; también pasó el banco de corte TCP y eliminó su base. Servidor y rol de prueba detenidos/eliminados. Estos resultados son PostgreSQL/psycopg, no ejecución de C# ni pruebas de la app Windows; las pruebas xUnit agregadas quedan pendientes.

### Verificación de Windows y corte real con Npgsql · 2026-11-10

- En Windows x64 con SDK .NET 10.0.403, el restore y build Release pasaron. El build reportó dos advertencias xUnit2013 preexistentes en `ProductsViewModelTests`.
- Pasaron las 36 pruebas de migraciones con PostgreSQL 18.6 en un clúster temporal en IPv4 loopback y un rol con CREATEDB sin superusuario. Cubren instalación única, repetición, rollback previo a confirmar, concurrencia entre instaladores, conservación de objetos existentes e historiales incompatibles.
- Una primera ejecución de la suite completa se interrumpió cuando dejó de mostrar avance. La repetición con `dotnet test tests/FrubberPOS.Tests/FrubberPOS.Tests.csproj -c Release --no-build --blame-hang-timeout 45s --blame-hang-dump-type none` terminó con **748 de 748 pruebas aprobadas**. Quedaron cero bases aleatorias y se detuvo y eliminó el clúster temporal.
- La nueva prueba xUnit usa el constructor normal del instalador. Un proxy TCP en loopback suprime la confirmación de `COMMIT` emitida por PostgreSQL y cierra la conexión de Npgsql. El instalador termina devolviendo un resultado incierto. Una conexión directa comprueba que hay una fila de historial y que el contador se creó en cero. Un segundo intento devuelve cero aplicaciones. Se habilita explícitamente mediante `FRUBBER_JHO136_ISOLATED=1` porque el proxy usa PostgreSQL local sin TLS.
- La verificación se limita al instalador aislado. La detección previa cubre los objetos enumerados en la consulta del catálogo. Quedan fuera de esa verificación los cambios a la configuración, propietario o permisos de la base y la integridad del esquema detrás de un historial reconocido. El destino debe ser nuevo, dedicado y de uso exclusivo durante la instalación.

### Operación de dos puestos con TLS en Windows · 2026-11-10

Spec Kit `specs/003-operacion-dos-puestos/` separa el arranque del escritorio de las comprobaciones de los casos de uso. El banco usó Windows x64, SDK .NET 10.0.403 y PostgreSQL 18.6 desechable. El instalador C# preparó la base vacía con 001. El administrador de pruebas aplicó 002, 003 y 005 en una transacción y registró dos roles con el procedimiento de alta. Los puestos se conectaron con Npgsql, SCRAM y TLS `VerifyFull`. Fallaron la conexión sin TLS y la conexión con nombre de host distinto del certificado.

- Cada rol obtuvo su propio puesto mediante la comprobación de inicio. La base conservó una sola fila de historial 2.0. El PIN incorrecto fue rechazado y los PIN válidos abrieron sesiones independientes con auditoría en el motor.
- Dos aperturas en el mismo puesto produjeron una caja y un conflicto. El segundo puesto abrió su propia caja. Se rechazaron escrituras directas en caja y ventas, lecturas directas del hash, contador e historial de migraciones y un intento de registrar una venta en la caja del otro puesto.
- Un fallo de inserción de línea después de asignar el número revirtió cabecera, líneas, inventario y contador. Dos puestos registraron 40 ventas concurrentes sobre inventario compartido, 20 por puesto. Los números quedaron del 1 al 40, el contador en 40 y el inventario bajó de 100.000 a 60.000 milésimas.
- El banco verificó el control de licencia con un doble de prueba. El ejecutable todavía arranca por el migrador anterior. La composición WPF queda pendiente de conectar la configuración protegida por DPAPI y la licencia firmada que autoriza dos cajas según ADR-030 y JHO-116. El ensayo no prueba recuperación idempotente, cierre de caja ni operación del ejecutable.

Para reproducir, crear un clúster desechable local con TLS y SCRAM, base vacía `frubber` y roles de puesto aún ausentes. El certificado debe validar `127.0.0.1` y no `localhost` para probar el rechazo por nombre. Exigir TLS para esa base. Usar un puerto de prueba distinto de 5432. Definir `FRUBBER_JHO136_ISOLATED=1` y `FRUBBER_JHO136_WORKSTATION_ADMIN` con la conexión temporal del administrador, `SSL Mode=VerifyFull` y la ruta al certificado. Ejecutar:

```powershell
dotnet test tests/FrubberPOS.Tests/FrubberPOS.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Postgres20WorkstationTests"
```

El guion 002 aún modifica roles globales, por lo que este banco se ejecuta únicamente en un clúster nuevo, dedicado y eliminable. Las credenciales de los puestos se crean para el ensayo y se mantienen en memoria.

El build Release pasó con cero errores y las dos advertencias xUnit2013 preexistentes. El banco dirigido pasó tras corregir la lectura de `user_role` en el directorio de PIN. La suite completa con el banco habilitado pasó **749 de 749** en 4 min 5 s. Quedaron cero bases aleatorias. Se detuvo y eliminó el clúster desechable con su certificado y sus roles.

### Arranque y cobro desde el ejecutable de Windows

La ruta `FrubberPOS.Desktop.exe --puesto "<archivo.cfg>"` carga la configuración con DPAPI de la cuenta actual. El archivo contiene JSON UTF-8 cifrado como bytes con ámbito `CurrentUser` y entropía UTF-8 `FrubberPOS.Workstation.v2`. Sus campos son `ConnectionString`, con el rol individual y `SSL Mode=VerifyFull`, y `ServerMachineCode`, con el código de 16 caracteres registrado por quien provisiona el puesto. Crear el archivo desde la cuenta Windows que ejecutará Frubber. En el banco, el servidor aplicó los guiones 001, 002, 003, 005 y 006. La función de 006 entrega al puesto activo la licencia firmada y su posición entre los puestos activos. El puesto verifica firma, código del servidor y cupo en cada inicio, apertura y cobro. FRB1 permite una caja. El cupo de FRB2 va dentro del contenido firmado.

La automatización de Windows ejecutó dos perfiles por separado en un mismo PC, cada uno con su propio archivo DPAPI y rol PostgreSQL sobre TLS. En ambos se inició sesión por PIN, se abrió caja, se agregó un producto por PLU y se cobró desde la pantalla. La primera venta mostró V-000001 y la segunda V-000002. PostgreSQL confirmó dos cajas abiertas de distintos puestos, dos ventas vinculadas a sus cajas y el descuento correspondiente en inventario.

El ejecutable rechazó al iniciar un archivo de configuración sin DPAPI. Con una FRB1 firmada de un cupo rechazó el inicio del segundo puesto. Al cambiar la licencia a esa FRB1 con el segundo puesto ya iniciado, rechazó por separado la apertura de caja y el cobro, sin crear otra caja ni venta. Cada operación se completó al restaurar la FRB2 firmada para dos cajas.

El ejecutable de laboratorio se compiló desde una copia temporal que llevaba la clave pública de prueba. Las licencias FRB1 y FRB2 se firmaron en memoria para esa prueba. El ejecutable compilado en el workspace conserva la clave pública del dueño. El banco eliminó su copia, el certificado, los archivos DPAPI y el clúster temporal. La evidencia cubre la pantalla operativa del puesto y los flujos anteriores. El aprovisionamiento del instalador, el control de cupo durante el alta y el despliegue en equipos distintos conservan sus tareas.

El build Release de la solución pasó con cero errores y dos avisos xUnit2013 preexistentes. El banco dirigido comprobó además el rechazo del cupo manipulado y de una licencia vinculada a otro servidor. La regresión completa pasó **749 de 749** en 4 min 16 s. Quedaron cero bases aleatorias y se eliminó el segundo clúster de pruebas.

### Cierre independiente por puesto

Para la versión que incluye el cierre, aplicar también `007_CierreCaja.sql` en el servidor antes de iniciar los puestos. Desde Venta, el cajero confirma **Cerrar caja** y la pantalla de apertura muestra el resumen de la sesión recién cerrada.

La prueba dirigida en Windows usó dos roles y dos modelos de pantalla con PostgreSQL desechable sobre TLS. El puesto A mostró $21.000 de efectivo esperado. El puesto B registró una transferencia de $900 al mismo tiempo y, tras el cierre de A, registró otra venta en efectivo. B mostró $23.000 de efectivo esperado y $900 de transferencias. La prueba también rechazó el cierre cruzado y comprobó que repetir el cierre anterior de A devuelve su resumen original incluso si A ya abrió una nueva caja. Se comprobó el texto que consume la ventana WPF mediante su modelo de pantalla.

El build Release pasó. En la regresión general, 744 pruebas pasaron, la prueba opt-in de puestos se omitió (pasó por separado) y cuatro pruebas de respaldo fallaron con la conexión local predeterminada. Al repetir respaldo y flujo crítico contra un PostgreSQL desechable configurado explícitamente con TLS, pasaron las 11 pruebas del grupo.

### Validación en dos equipos y revisión de cierre · 2026-11-11

Frubber 2.0.0 lab.1 inició en dos PC Windows con perfiles DPAPI, roles individuales, TLS verificado y una licencia FRB2 firmada para dos cajas. A cerró con una venta de 1.500 COP. B registró tres ventas por 6.000 COP después del cierre de A y luego cerró su propia caja. PostgreSQL confirmó los totales separados y el orden de los eventos. El dueño aceptó Tailscale como evidencia de T012.

Una consulta de solo lectura con el rol de A y TLS verificado encontró el producto de prueba y las cuatro filas de la vista autorizada de historial. El rol carece de SELECT directo en usuarios, INSERT en ventas y UPDATE en caja. En esa versión del instalador el menú administrativo todavía estaba pendiente.

La compilación Release en Daniel terminó con cero errores y dos avisos xUnit2013 preexistentes.

Se repitió `Postgres20WorkstationTests` con la compilación Release en un clúster nuevo de loopback con TLS. Pasó la prueba dirigida: inicialización 2.0, PIN, licencia, permisos, reversión del cobro fallido, 40 ventas concurrentes numeradas del 1 al 40 y cierre independiente. Se detuvo y eliminó ese clúster temporal.

La regresión de Windows pasó con 747 aprobadas, cero fallos y dos pruebas de activación explícita omitidas en 3 min 29 s. La de dos puestos pasó por separado en esta revisión. La de corte durante el COMMIT del instalador conserva la evidencia anterior. Se eliminó la base temporal de regresión.

### Productos, Usuarios e Historial · 2026-11-17

Para el menú administrativo de la 2.0, aplicar en el servidor `008_AdministracionPuesto.sql` y `009_ProductosUsuariosPuesto.sql` en ese orden antes de iniciar los puestos con los binarios correspondientes. Al ingresar con PIN de administrador aparecen Productos, Usuarios e Historial. Las escrituras se ejecutan por funciones autorizadas y el detalle de venta conserva los datos del momento de cobro.

La integración dirigida abrió las vistas WPF con datos de PostgreSQL 18.6 por TLS en el clúster desechable. Verificó las operaciones de Productos y Usuarios, el detalle de solo lectura, la visibilidad por rol, los rechazos de permisos en SQL y la protección del último administrador. Se mantuvieron los resultados de cierre y cobro de T012 en los dos equipos. Para los resultados detallados, véase T013 en `specs/003-operacion-dos-puestos/tasks.md`.
