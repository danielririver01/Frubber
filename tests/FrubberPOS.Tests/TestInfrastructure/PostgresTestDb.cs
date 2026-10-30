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
                           SystemSequence RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
