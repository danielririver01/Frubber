# JHO-136 · Integración progresiva de la línea base 2.0

## Estado

La aplicación todavía usa los repositorios y migraciones anteriores: **no es compatible con la línea base 2.0 y no está lista para el piloto ni para distribuirse**. No se deben cambiar las instalaciones ni las bases existentes. La migración de datos del cliente es una tarea separada; este trabajo no la ejecuta.

Se agregó un instalador de la línea base 2.0, aislado de la composición de la aplicación y destinado al servidor. No se ejecuta al abrir Frubber. Su alcance es exclusivamente `001_Baseline.sql`, embebido directamente desde `database/postgresql/` para no mantener dos copias. No instala seguridad, no da de alta puestos ni migra datos. En una base nueva y dedicada crea el esquema y registra nombre y checksum en la misma transacción. Serializa ejecutores que usen ese mismo instalador; el administrador debe garantizar que ninguna otra herramienta modifique la base durante la instalación. Rechaza bases no vacías, historial desconocido o modificado y mezclas con el historial anterior. Un registro válido existente devuelve cero sin reaplicar el guion; **esto no verifica que el resto del esquema no haya sido modificado**.

Como protección temporal, el migrador usado por la aplicación anterior rechaza la presencia de `public.schema_migration`, incluso si está vacío o coexiste con el registro anterior. Esto evita intentar aplicar las migraciones anteriores sobre una base 2.0; no convierte la aplicación en compatible con ella. Ambos ejecutores están diseñados para `public`; no utilizar esquemas alternativos para instalar Frubber.

## Seguridad antes de conectar el instalador

`002_Security.sql` **no se aplica desde este instalador**. Actualmente contiene referencias literales a la base `frubber` y al rol `postgres`, crea o reutiliza roles de clúster y puede eliminar el rol de prototipo `frubber_puesto_test`. Por eso no debe ejecutarse automáticamente ni en un servidor compartido sin revisar y corregir esas operaciones globales y probar el aprovisionamiento. La protección DPAPI, el transporte seguro, los permisos y las operaciones autorizadas siguen siendo requisitos de distribución.

`003_RegistroVenta.sql` aún no vincula el usuario de caja a una autenticación de PIN en el servidor y rechaza solicitudes repetidas sin devolver la venta previa. No conectar esa operación como flujo seguro completo; la recuperación idempotente corresponde a JHO-114. Caja por puesto, productos, usuarios, historial, licencia, inicialización del servidor, migración de clientes y build/suite Windows siguen pendientes.

## Pruebas

`Postgres20BaselineInstallerTests` crea únicamente bases temporales aleatorias mediante `PostgresTestDb.CreateScratchAsync`; requiere un PostgreSQL de pruebas y un usuario con permiso CREATEDB. No usar una base o servidor de producción. La clase cubre instalación/repetición, rechazo cruzado de migradores, objetos y datos preexistentes, checksum e historial desconocido, historial mezclado y dos instaladores concurrentes. En Windows:

```powershell
# Configurar FRUBBERPOS_TEST_CONNECTION_STRING para un servidor desechable de pruebas.
dotnet test tests/FrubberPOS.Tests/FrubberPOS.Tests.csproj -c Release --filter "FullyQualifiedName~Postgres20BaselineInstallerTests"
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
