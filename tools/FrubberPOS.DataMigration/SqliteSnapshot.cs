using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.DataMigration;

internal sealed record SqliteSnapshot(string Sha256, Dictionary<string, List<Dictionary<string, object?>>> Tables)
{
    public List<Dictionary<string, object?>> this[string table] => Tables[table];

    public static async Task<SqliteSnapshot> ReadAsync(string path, CancellationToken token)
    {
        var wal = path + "-wal";
        if (File.Exists(wal) && new FileInfo(wal).Length > 0)
            throw new InvalidDataException("Use un respaldo SQLite consolidado antes de migrar.");
        var before = await HashAsync(path, token);
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ConnectionString);
        await source.OpenAsync(token);
        await source.ExecuteAsync(new CommandDefinition("PRAGMA query_only=ON;", cancellationToken:token));
        using var transaction = source.BeginTransaction(deferred:true);
        var integrity = (await source.QueryAsync<string>(new CommandDefinition("PRAGMA integrity_check;", transaction:transaction, cancellationToken:token))).ToArray();
        if (integrity is not ["ok"] || (await source.QueryAsync(new CommandDefinition("PRAGMA foreign_key_check;", transaction:transaction, cancellationToken:token))).Any())
            throw new InvalidDataException("El respaldo no supera la revisión de integridad.");

        await using var reference = new SqliteConnection("Data Source=:memory:");
        await reference.OpenAsync(token);
        await reference.ExecuteAsync(new CommandDefinition(await ReadResourceAsync("SourceSchema.sql", token), cancellationToken:token));
        const string schemaSql = "SELECT type AS Kind,name AS Name,sql AS Sql FROM sqlite_master WHERE sql IS NOT NULL AND name NOT GLOB 'sqlite_*' ORDER BY type,name;";
        var actual = (await source.QueryAsync<SchemaItem>(new CommandDefinition(schemaSql,transaction:transaction,cancellationToken:token))).ToArray();
        var expected = (await reference.QueryAsync<SchemaItem>(new CommandDefinition(schemaSql,cancellationToken:token))).ToArray();
        if (!actual.Select(Normalize).SequenceEqual(expected.Select(Normalize)))
            throw new InvalidDataException("El esquema del respaldo no coincide con la versión 1.0 admitida.");

        var tables = new Dictionary<string,List<Dictionary<string,object?>>>();
        foreach (var table in TableMaps.All.Select(map => map.Source).Concat(["LicenseState","SystemSequence","SchemaMigration"]))
        {
            await using var command = source.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT * FROM \"{table}\";";
            await using var rows = await command.ExecuteReaderAsync(token);
            var values = new List<Dictionary<string,object?>>();
            while (await rows.ReadAsync(token))
            {
                var row = new Dictionary<string,object?>();
                for (var index=0; index<rows.FieldCount; index++)
                    row.Add(rows.GetName(index),rows.IsDBNull(index) ? null : rows.GetValue(index));
                values.Add(row);
            }
            tables.Add(table,values);
        }
        var versions = tables["SchemaMigration"].Select(row => (long)row["Version"]!).Order().ToArray();
        if (!versions.SequenceEqual(new long[] {1,2,3,4}))
            throw new InvalidDataException("Se requiere un respaldo de la 1.0 con sus cuatro migraciones registradas.");
        if (await HashAsync(path,token) != before || (File.Exists(wal) && new FileInfo(wal).Length > 0))
            throw new InvalidDataException("El archivo de origen cambió durante la lectura. Cree un respaldo nuevo.");
        return new(before,tables);
    }

    private static SchemaItem Normalize(SchemaItem item) => item with { Sql = item.Sql.Replace("\r\n","\n").Trim() };
    private sealed record SchemaItem(string Kind, string Name, string Sql);

    public static async Task<string> ReadResourceAsync(string name, CancellationToken token)
    {
        using var stream = typeof(SqliteSnapshot).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Falta un recurso de migración.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(token);
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream,token));
    }
}
