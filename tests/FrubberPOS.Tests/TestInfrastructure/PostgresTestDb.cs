using FrubberPOS.Application.Persistence;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using Npgsql;

namespace FrubberPOS.Tests.TestInfrastructure;

/// <summary>
/// Helper para pruebas de integración contra PostgreSQL real (JHO-113).
/// Lee la cadena de conexión de <c>FRUBBERPOS_TEST_CONNECTION_STRING</c>;
/// si falta, usa una cadena de desarrollo local.
/// </summary>
public static class PostgresTestDb
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("FRUBBERPOS_TEST_CONNECTION_STRING")
        ?? "Host=localhost;Port=5432;Database=frubberpos_test;Username=frubber;Password=frubber;";

    /// <summary>
    /// Ejecuta las migraciones contra la BD de prueba (idempotente: si ya
    /// están aplicadas, devuelve 0 y no cambia nada).
    /// </summary>
    public static async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var runner = new MigrationRunner();
        var result = await runner.MigrateAsync(ConnectionString, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"No se pudo migrar la base de prueba: {result.Error?.Message}");
        }

        await InstallIdentitySyncAsync(cancellationToken);
    }

    // JHO-113: las pruebas siembran filas con id explícito (UserId 1,
    // CashSessionId 1, SaleId 1…) y después el caso de uso inserta con id
    // autogenerado. En SQLite el id lo calculaba la propia tabla, pero en
    // PostgreSQL la secuencia identity se queda en 1 y el INSERT normal
    // choca con la fila sembrada (23505 «llave duplicada»), que era la
    // causa de ~20 fallos en cascada. Este disparador de sentencia (solo
    // en la BD de prueba; producción nunca inserta ids explícitos) deja la
    // secuencia en max(id) tras cada INSERT, de modo que el siguiente id
    // autogenerado siempre es superior a todo lo sembrado.
    private const string IdentitySyncFunctionSql = """
        CREATE OR REPLACE FUNCTION public.frubber_test_sync_identity() RETURNS trigger
        LANGUAGE plpgsql AS $fn$
        DECLARE
            v_seq text;
            v_max bigint;
        BEGIN
            v_seq := pg_get_serial_sequence(format('%I.%I', TG_TABLE_SCHEMA, TG_TABLE_NAME), TG_ARGV[0]);
            EXECUTE format('SELECT COALESCE(max(%I), 0) FROM %I.%I',
                           TG_ARGV[0], TG_TABLE_SCHEMA, TG_TABLE_NAME)
                INTO v_max;
            IF v_max = 0 THEN
                PERFORM setval(v_seq, 1, false);
            ELSE
                PERFORM setval(v_seq, v_max, true);
            END IF;
            RETURN NULL;
        END;
        $fn$;
        """;

    private const string IdentitySyncTriggerSql = """
        DO $do$
        DECLARE t record;
        BEGIN
          FOR t IN
            SELECT c.relname AS tbl, a.attname AS col
              FROM pg_attribute a
              JOIN pg_class c ON c.oid = a.attrelid
              JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'public' AND c.relkind = 'r'
               AND a.attnum > 0 AND NOT a.attisdropped
               AND a.attidentity <> ''
          LOOP
            EXECUTE format('DROP TRIGGER IF EXISTS frubber_sync_identity ON public.%I', t.tbl);
            EXECUTE format('CREATE TRIGGER frubber_sync_identity AFTER INSERT ON public.%I'
                           || ' FOR EACH STATEMENT EXECUTE FUNCTION public.frubber_test_sync_identity(%L)',
                           t.tbl, t.col);
          END LOOP;
        END
        $do$;
        """;

    private static async Task InstallIdentitySyncAsync(CancellationToken cancellationToken)
    {
        await using var connection = NewConnection();
        await connection.OpenAsync(cancellationToken);
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = IdentitySyncFunctionSql;
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        // JHO-113: el bloque descubre las columnas identity en el catálogo y
        // hace DROP IF EXISTS + CREATE por tabla, así que es idempotente y se
        // reejecuta en cada migración. Antes se saltaba con un umbral fijo
        // (>= 5 disparadores): al añadir una tabla con identidad el umbral
        // daba por buena la instalación y la nueva tabla quedaba sin cubrir.
        await using (var triggers = connection.CreateCommand())
        {
            triggers.CommandText = IdentitySyncTriggerSql;
            await triggers.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public static NpgsqlConnection NewConnection() =>
        new(ConnectionString);

    public static NpgsqlUnitOfWorkFactory NewUnitFactory() =>
        new(new NpgsqlConnectionFactory(new DatabaseOptions(ConnectionString)));

    /// <summary>
    /// Limpia todas las tablas de datos (mantiene SchemaMigration) para
    /// dejar la BD lista para el siguiente test.
    /// </summary>
    public static async Task CleanAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = NewConnection();
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            TRUNCATE TABLE SaleVoid, SaleLine, Sale, CashSession, Product, ""User"",
                           ScaleSetting, BusinessSetting, LicenseState, TrialState,
                           SystemSequence RESTART IDENTITY CASCADE;
            INSERT INTO SystemSequence (SequenceName, LastValue) VALUES ('SaleNumber', 0);
            INSERT INTO BusinessSetting (BusinessSettingId) VALUES (1);
            INSERT INTO ScaleSetting (ScaleSettingId) VALUES (1);";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Crea una base de datos desechable (JHO-113). Necesaria para las
    /// pruebas del ejecutor de migraciones, que exigen una base recién
    /// creada; la base se elimina al cerrar.
    /// </summary>
    public static async Task<ScratchDatabase> CreateScratchAsync(CancellationToken cancellationToken = default)
    {
        var name = "frubberpos_scratch_" + Guid.NewGuid().ToString("N");
        var adminBuilder = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres" };

        await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync(cancellationToken);
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\";";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        return new ScratchDatabase(
            name,
            new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString,
            adminBuilder.ConnectionString);
    }
}

/// <summary>Base de datos temporal para pruebas del ejecutor de migraciones.</summary>
public sealed class ScratchDatabase : IAsyncDisposable
{
    private readonly string name;
    private readonly string adminConnectionString;

    internal ScratchDatabase(string name, string connectionString, string adminConnectionString)
    {
        this.name = name;
        this.adminConnectionString = adminConnectionString;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public NpgsqlConnection NewConnection() => new(ConnectionString);

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);";
        await drop.ExecuteNonQueryAsync();
    }
}
