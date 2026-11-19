using Dapper;
using Npgsql;

namespace FrubberPOS.DataMigration;

internal sealed class ImportWriter(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
{
    public Task<int> ExecuteAsync(string sql, object? parameters = null) =>
        connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: token));

    public async Task<T> ScalarAsync<T>(string sql, object? parameters = null) =>
        (await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, parameters, transaction, cancellationToken: token)))!;

    public async Task RequireEmptyAsync()
    {
        foreach (var map in TableMaps.All.Where(map => map != TableMaps.Settings))
            if (await ScalarAsync<long>($"SELECT count(*) FROM public.{map.Target};") != 0)
                throw new InvalidDataException("El destino ya contiene datos. Use una base nueva dedicada.");

        if (!await ScalarAsync<bool>("""
            SELECT (SELECT count(*) FROM public.license_state) = 0
               AND (SELECT count(*) FROM public.terminal) = 1
               AND (SELECT count(*) FROM public.ticket_counter WHERE scope='venta' AND last_number=0) = 1
               AND (SELECT count(*) FROM public.business_setting WHERE business_name IS NULL AND tax_identifier IS NULL
                   AND phone IS NULL AND address IS NULL AND ticket_footer_message IS NULL AND printer_name IS NULL) = 1;
            """))
            throw new InvalidDataException("El destino ya está configurado. Use una base nueva dedicada.");
    }

    public Task<int> InsertAsync(TableMap map, Dictionary<string, object?> row,
        Dictionary<string, object?>? overrides = null, Dictionary<string, object?>? extra = null)
    {
        var values = Parameters(map, row);
        if (overrides is not null)
            foreach (var change in overrides)
                values[change.Key] = change.Value;
        var columns = map.Columns.Select(column => column.Target).ToList();
        var names = map.Columns.Select(column => "@" + column.Source).ToList();
        if (extra is not null)
            foreach (var item in extra)
            {
                columns.Add(item.Key);
                names.Add("@" + item.Key);
                values.Add(item.Key, item.Value);
            }
        return ExecuteAsync($"INSERT INTO public.{map.Target} ({string.Join(',', columns)}) VALUES ({string.Join(',', names)});", values);
    }

    public Task<int> UpdateAsync(TableMap map, Dictionary<string, object?> row)
    {
        var key = map.Columns[0];
        var assignments = map.Columns.Skip(1).Select(column => $"{column.Target}=@{column.Source}");
        return ExecuteAsync($"UPDATE public.{map.Target} SET {string.Join(',', assignments)} WHERE {key.Target}=@{key.Source};", Parameters(map, row));
    }

    public async Task<bool> VerifyAsync(TableMap map, List<Dictionary<string, object?>> expected)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {string.Join(',', map.Columns.Select(column => column.Target))} FROM public.{map.Target} ORDER BY {map.Columns[0].Target};";
        await using var rows = await command.ExecuteReaderAsync(token);
        foreach (var row in expected.OrderBy(row => (long)row[map.Columns[0].Source]!))
        {
            if (!await rows.ReadAsync(token))
                return false;
            for (var index = 0; index < map.Columns.Count; index++)
            {
                var column = map.Columns[index];
                var value = ConvertValue(column.Source, row[column.Source]);
                var actual = rows.IsDBNull(index) ? null : rows.GetValue(index);
                if (!SameValue(value, actual))
                    return false;
            }
        }
        return !await rows.ReadAsync(token);
    }

    public async Task RestartSequenceAsync(TableMap map, List<Dictionary<string, object?>> rows)
    {
        var column = map.Columns[0];
        var maximum = rows.Select(row => (long)row[column.Source]!).DefaultIfEmpty(0).Max();
        var next = checked(Math.Max(0, maximum) + 1);
        var sequence = await ScalarAsync<string>("SELECT pg_get_serial_sequence(@Table,@Column);",
            new { Table = "public." + map.Target, Column = column.Target });
        if (sequence != $"public.{map.Target}_{column.Target}_seq")
            throw new InvalidDataException("La secuencia del destino difiere de la línea base.");
        // RESTART participates in the transaction.
        await ExecuteAsync(FormattableString.Invariant($"ALTER SEQUENCE {sequence} RESTART WITH {next};"));
    }

    private static Dictionary<string, object?> Parameters(TableMap map, Dictionary<string, object?> row) =>
        map.Columns.ToDictionary(column => column.Source, column => ConvertValue(column.Source, row[column.Source]));

    private static object? ConvertValue(string column, object? value)
    {
        if (value is null)
            return null;
        if (column.EndsWith("UtcMs", StringComparison.Ordinal))
            return DateTimeOffset.FromUnixTimeMilliseconds((long)value).UtcDateTime;
        if (column is "IsActive" or "UnitLocked" or "IsEnabled")
            return (long)value != 0;
        return value;
    }

    private static bool SameValue(object? expected, object? actual)
    {
        if (expected is byte[] left && actual is byte[] right)
            return left.AsSpan().SequenceEqual(right);
        if (expected is long number && actual is short or int or long)
            return number == Convert.ToInt64(actual);
        return Equals(expected, actual);
    }
}
