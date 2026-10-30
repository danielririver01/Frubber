# Pruebas de Frubber POS (JHO-113)

La suite corre **contra un PostgreSQL real**, no contra una base en memoria:
las migraciones, los disparadores de inmutabilidad, los índices y el
`MigrationRunner` se prueban tal como quedan en producción.

## Requisitos

- Windows x64 (el proyecto de pruebas es `net10.0-windows` porque valida WPF).
- .NET 10 SDK.
- PostgreSQL **16 o superior** instalado y corriendo en `localhost:5432`.
- Rol `frubber` con `CREATEDB` y base `frubberpos_test`.

Preparar el entorno (idempotente):

```powershell
.\scripts\preparar-bd-pruebas.ps1 -PgBin "C:\Program Files\PostgreSQL\16\bin"
```

`CREATEDB` no es opcional: `PostgresTestDb.CreateScratchAsync` crea una base
desechable por prueba para el ejecutor de migraciones (esas pruebas exigen una
base recién creada) y la borra al terminar.

## Cadena de conexión

`PostgresTestDb` usa, por orden:

1. `FRUBBERPOS_TEST_CONNECTION_STRING` (variable de entorno).
2. `Host=localhost;Port=5432;Database=frubberpos_test;Username=frubber;Password=frubber;`

```powershell
$env:FRUBBERPOS_TEST_CONNECTION_STRING = "Host=localhost;Port=5432;Database=frubberpos_test;Username=frubber;Password=frubber;"
```

## Ejecutar

```powershell
dotnet test tests\FrubberPOS.Tests\FrubberPOS.Tests.csproj -c Release
```

Con detalle por prueba:

```powershell
dotnet test tests\FrubberPOS.Tests\FrubberPOS.Tests.csproj -c Release --logger "console;verbosity=normal" 2>&1 | Tee-Object -FilePath "$env:TEMP\frubber-tests.txt"
```

## Cómo se aísla la base compartida

- **Sin paralelismo**: `AssemblyInfo.cs` fija
  `CollectionBehavior(DisableTestParallelization = true)`. Todas las clases
  comparten `frubberpos_test`; en paralelo se truncarían entre sí.
- **Limpieza doble**: los *testbeds* (`ProductTestBed`, `CashTestBed`,
  `AuthenticationTestBed`) limpian **al crear y al destruir**
  (`CleanAllAsync`). La limpieza al crear es la que evita que un crash a mitad
  de corrida contamine las clases siguientes.
- **Datos iniciales**: `CleanAllAsync` trunca con `RESTART IDENTITY` y vuelve a
  sembrar `SystemSequence` (`SaleNumber`), `BusinessSetting` y `ScaleSetting`.
- **Ids sembrados a mano**: muchas pruebas insertan filas con id explícito
  (`UserId = 1`, …). En PostgreSQL eso no mueve la secuencia identity, y el
  siguiente id autogenerado chocaba con la fila sembrada. `PostgresTestDb`
  instala un disparador (`frubber_sync_identity`) **solo en la base de
  pruebas** que deja la secuencia en `max(id)` tras cada `INSERT`. El bloque
  descubre las tablas con identidad desde el catálogo, así que una tabla nueva
  queda cubierta sin tocar el código.

## Clases de prueba y ciclo de vida

- xUnit **v2 no invoca `IAsyncDisposable`** en las clases de prueba. Para
  limpiar entre casos hay que implementar `IAsyncLifetime`
  (`InitializeAsync` / `DisposeAsync`) o usar un testbed con `await using`.
  `IDisposable` sí se invoca.
- Los testbeds son `IAsyncDisposable` y se usan como variable local:
  `await using var bed = await ProductTestBed.CreateAsync();`

## Respaldo

`BackupTests` ejecuta `pg_dump` de verdad. Si el binario no está en el PATH,
`PostgresBackupService` lo busca junto a `DatabaseOptions`/instalación; en una
máquina sin PostgreSQL las pruebas de respaldo fallan por entorno, no por
código.
