using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Domain.Licensing;
using FrubberPOS.Infrastructure.Licensing;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using Npgsql;

namespace FrubberPOS.DataMigration;

public sealed record ImportSqliteRequest(string SourcePath, string SignedLicense, string ServerMachineCode, string ConnectionString);
public sealed record MigrationTableCheck(string Table, long Rows, bool ValuesMatch);
public sealed record MigrationReport(string SourceSha256, IReadOnlyList<MigrationTableCheck> Tables, bool LicenseVerified,
    bool SourceLicenseSealChecked, int LegacyOpenCashSessions);

public sealed class ImportSqliteUseCase(EcdsaLicenseSignatureVerifier verifier)
{
    public async Task<Result<MigrationReport>> ExecuteAsync(ImportSqliteRequest request, CancellationToken cancellationToken)
    {
        var commitAttempted = false;
        try
        {
            var options = new NpgsqlConnectionStringBuilder(request.ConnectionString)
            {
                IncludeErrorDetail = false, PersistSecurityInfo = false, LogParameters = false,
            };
            if (options.SslMode != SslMode.VerifyFull)
                return Fail(ErrorCode.Validation, "La conexión de destino debe usar TLS VerifyFull.");
            var source = await SqliteSnapshot.ReadAsync(request.SourcePath, cancellationToken);
            var counter = ValidateBalances(source);
            var sealChecked = ValidateLicense(source, request);
            var installed = await new Postgres20BaselineInstaller().InstallAsync(
                new NpgsqlConnectionFactory(new DatabaseOptions(options.ConnectionString)), cancellationToken);
            if (installed.IsFailure)
                return Result<MigrationReport>.Failure(installed.Error!);

            await using var target = new NpgsqlConnection(options.ConnectionString);
            await target.OpenAsync(cancellationToken);
            await using var transaction = await target.BeginTransactionAsync(cancellationToken);
            var writer = new ImportWriter(target, transaction, cancellationToken);
            await writer.ExecuteAsync("SELECT pg_catalog.pg_advisory_xact_lock(1179796818,124); SET LOCAL search_path=pg_catalog,public,pg_temp;");
            await writer.ExecuteAsync(await SqliteSnapshot.ReadResourceAsync("LegacyState.sql", cancellationToken));
            var tables = TableMaps.All.Select(map => "public." + map.Target)
                .Concat(["public.terminal", "public.ticket_counter", "public.license_state"]);
            await writer.ExecuteAsync("LOCK TABLE " + string.Join(',', tables) + " IN EXCLUSIVE MODE;");
            await writer.RequireEmptyAsync();
            var terminal = await writer.ScalarAsync<long>("SELECT terminal_id FROM public.terminal WHERE machine_code='0000000000000000' AND NOT is_active;");
            if (terminal <= 0)
                throw new InvalidDataException("Falta el puesto histórico definido por la línea base.");

            await ImportHistoryAsync(writer, source, terminal);
            foreach (var setting in source["BusinessSetting"])
                await writer.UpdateAsync(TableMaps.Settings, setting);
            foreach (var stateMap in new[] { TableMaps.Trial, TableMaps.Scale })
                foreach (var row in source[stateMap.Source])
                    await writer.InsertAsync(stateMap, row);

            await writer.ExecuteAsync("UPDATE public.ticket_counter SET last_number=@Number WHERE scope='venta';", new { Number = counter });
            var licenseRow = source["LicenseState"].SingleOrDefault();
            if (licenseRow is not null)
            {
                var updated = DateTimeOffset.FromUnixTimeMilliseconds((long)licenseRow["UpdatedAtUtcMs"]!).UtcDateTime;
                await writer.ExecuteAsync("""
                    INSERT INTO public.license_state(license_state_id,payload_version,signed_payload,updated_at)
                    VALUES(1,1,@Payload,@Updated);
                    """, new { Payload = request.SignedLicense.Trim(), Updated = updated });
            }

            var checks = new List<MigrationTableCheck>();
            foreach (var map in TableMaps.All)
            {
                if (!await writer.VerifyAsync(map, source[map.Source]))
                    return Fail(ErrorCode.Conflict, "El cuadre no coincide. Se revirtió la importación.");
                checks.Add(new(map.Target, source[map.Source].Count, true));
            }
            var licenseMatches = licenseRow is null
                ? await writer.ScalarAsync<long>("SELECT count(*) FROM public.license_state;") == 0
                : await writer.ScalarAsync<string>("SELECT signed_payload FROM public.license_state;") == request.SignedLicense.Trim();
            if (!licenseMatches || await writer.ScalarAsync<long>("SELECT last_number FROM public.ticket_counter WHERE scope='venta';") != counter ||
                await writer.ScalarAsync<long>("SELECT count(*) FROM public.cash_session WHERE terminal_id <> @Terminal;", new { Terminal = terminal }) != 0)
                return Fail(ErrorCode.Conflict, "La licencia, el contador o el puesto histórico no cuadran. Se revirtió la importación.");
            checks.Add(new("license_state", source["LicenseState"].Count, true));
            checks.Add(new("ticket_counter", 1, true));
            foreach (var map in new[] { TableMaps.Users, TableMaps.Products, TableMaps.Cash, TableMaps.Sales, TableMaps.Lines })
                await writer.RestartSequenceAsync(map, source[map.Source]);

            cancellationToken.ThrowIfCancellationRequested();
            commitAttempted = true;
            await transaction.CommitAsync(cancellationToken);
            return Result<MigrationReport>.Success(new(source.Sha256, checks, licenseRow is not null,
                sealChecked, source["CashSession"].Count(row => row["ClosedAtUtcMs"] is null)));
        }
        catch (Exception ex) when (commitAttempted && ex is (DbException or IOException or TimeoutException or OperationCanceledException or InvalidOperationException))
        {
            return Fail(ErrorCode.Connection, "El resultado de la confirmación es desconocido. Revise el destino antes de repetir la migración.");
        }
        catch (InvalidDataException ex)
        {
            return Fail(ErrorCode.Validation, ex.Message);
        }
        catch (Exception ex) when (ex is DbException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException or TimeoutException)
        {
            return Fail(ErrorCode.Storage, "La importación no se confirmó. Compruebe el respaldo, la conexión y los permisos del destino.");
        }
    }

    private static async Task ImportHistoryAsync(ImportWriter writer, SqliteSnapshot source, long terminal)
    {
        // The original actors can have changed role or activity since recording the sale.
        // Temporary values stay private to the transaction and are verified after restoration.
        foreach (var user in source["User"])
            await writer.InsertAsync(TableMaps.Users, user, new() { ["Role"] = "ADMIN", ["IsActive"] = true });
        foreach (var product in source["Product"])
            await writer.InsertAsync(TableMaps.Products, product);

        var sales = source["Sale"].ToLookup(row => (long)row["CashSessionId"]!);
        var lines = source["SaleLine"].ToLookup(row => (long)row["SaleId"]!);
        var voids = source["SaleVoid"].ToLookup(row => (long)row["SaleId"]!);
        foreach (var cash in source["CashSession"].OrderBy(row => row["ClosedAtUtcMs"] is null))
        {
            var opening = TableMaps.Cash.Columns.Where(column => column.Target.StartsWith("closed_", StringComparison.Ordinal))
                .ToDictionary(column => column.Source, _ => (object?)null);
            await writer.InsertAsync(TableMaps.Cash, cash, opening, new() { ["terminal_id"] = terminal });
            foreach (var sale in sales[(long)cash["CashSessionId"]!])
            {
                var saleId = (long)sale["SaleId"]!;
                var identity = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{source.Sha256}:{saleId}")));
                await writer.InsertAsync(TableMaps.Sales, sale, extra: new() { ["client_request_id"] = new Guid(identity.AsSpan(0, 16)) });
                foreach (var line in lines[saleId])
                    await writer.InsertAsync(TableMaps.Lines, line);
                foreach (var saleVoid in voids[saleId])
                    await writer.InsertAsync(TableMaps.Voids, saleVoid);
            }
            if (cash["ClosedAtUtcMs"] is not null)
                await writer.UpdateAsync(TableMaps.Cash, cash);
        }
        foreach (var user in source["User"])
            await writer.UpdateAsync(TableMaps.Users, user);
    }

    private bool ValidateLicense(SqliteSnapshot source, ImportSqliteRequest request)
    {
        if (source["LicenseState"].Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(request.SignedLicense))
                throw new InvalidDataException("El respaldo no contiene una licencia activada.");
            return false;
        }
        if (!LicenseKeyFormat.TryParse(request.SignedLicense, out var license) || license!.MachineCode != request.ServerMachineCode ||
            license.IssuedDate > DateOnly.FromDateTime(DateTime.UtcNow) ||
            !verifier.Verify(license.KeyId, license.MachineCode, license.IssuedDate, license.Signature))
            throw new InvalidDataException("Se requiere la licencia FRB1 firmada y válida para el equipo servidor indicado.");

        byte[] plain;
        try
        {
            plain = new DpapiLicenseProtector().Unprotect((byte[])source["LicenseState"].Single()["ProtectedPayload"]!);
        }
        catch (CryptographicException)
        {
            // A portable backup can be read on another Windows account. The supplied FRB1
            // still needs a trusted signature and the explicit destination server identity.
            return false;
        }
        try
        {
            if (Encoding.UTF8.GetString(plain) != LicenseKeyFormat.BuildCanonicalPayload(license.KeyId, license.MachineCode, license.IssuedDate))
                throw new InvalidDataException("La licencia firmada no corresponde al sello del respaldo.");
            return true;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static long ValidateBalances(SqliteSnapshot source)
    {
        var sequences = source["SystemSequence"];
        var latest = source["Sale"].Select(row => (long)row["SaleNumber"]!).DefaultIfEmpty(0).Max();
        if (sequences.Count != 1 || !Equals(sequences[0]["SequenceName"], "SaleNumber") ||
            !Equals(sequences[0]["LastValue"], latest) || source["BusinessSetting"].Count != 1)
            throw new InvalidDataException("El contador o la configuración del respaldo no cuadran.");

        var lines = source["SaleLine"].ToLookup(row => (long)row["SaleId"]!);
        foreach (var sale in source["Sale"])
        {
            var items = lines[(long)sale["SaleId"]!];
            if (!items.Any() || items.Sum(row => (long)row["LineTotalCop"]!) != (long)sale["SubtotalCop"]!)
                throw new InvalidDataException("Las líneas y los totales de venta del respaldo no cuadran.");
        }

        var voids = source["SaleVoid"].Select(row => (long)row["SaleId"]!).ToHashSet();
        var activeSales = source["Sale"].Where(row => !voids.Contains((long)row["SaleId"]!)).ToLookup(row => (long)row["CashSessionId"]!);
        foreach (var cash in source["CashSession"].Where(row => row["ClosedAtUtcMs"] is not null))
        {
            var sales = activeSales[(long)cash["CashSessionId"]!];
            var cashTotal = sales.Where(row => Equals(row["PaymentMethod"], "CASH")).Sum(row => (long)row["TotalCop"]!);
            var transferTotal = sales.Where(row => Equals(row["PaymentMethod"], "TRANSFER")).Sum(row => (long)row["TotalCop"]!);
            if (!Equals(cash["ClosedCashSalesCop"], cashTotal) || !Equals(cash["ClosedTransferSalesCop"], transferTotal) ||
                !Equals(cash["ClosedDiscountCop"], sales.Sum(row => (long)row["DiscountCop"]!)) ||
                !Equals(cash["ClosedNetSalesCop"], checked(cashTotal + transferTotal)) ||
                !Equals(cash["ClosedExpectedCashCop"], checked((long)cash["OpeningBalanceCop"]! + cashTotal)) ||
                !Equals(cash["ClosedSaleCount"], sales.LongCount()))
                throw new InvalidDataException("Los resúmenes de caja del respaldo no cuadran con las ventas vigentes.");
        }
        return latest;
    }

    private static Result<MigrationReport> Fail(ErrorCode code, string message) =>
        Result<MigrationReport>.Failure(new Error(code, message));
}
