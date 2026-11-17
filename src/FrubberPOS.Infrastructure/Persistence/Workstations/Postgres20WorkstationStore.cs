using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Application.Users;
using FrubberPOS.Application.Workstations;
using FrubberPOS.Domain.Products;
using FrubberPOS.Domain.Users;
using FrubberPOS.Infrastructure.Security;
using Npgsql;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

public sealed class Postgres20WorkstationStore : IWorkstationStore
{
    private readonly NpgsqlConnectionFactory connections;
    internal Guid? AdminToken { get; private set; }

    public Postgres20WorkstationStore(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (new NpgsqlConnectionStringBuilder(options.ConnectionString).SslMode != SslMode.VerifyFull)
        {
            throw new ArgumentException("La conexión del puesto debe verificar el certificado y el nombre del servidor.", nameof(options));
        }
        connections = new NpgsqlConnectionFactory(options);
    }

    public Task<Result<WorkstationInfo>> CheckAsync(CancellationToken cancellationToken) =>
        QueryAsync(async connection =>
        {
            const string sql = """
                SELECT terminal_id AS Id, terminal_name AS Name
                FROM public.terminal_info()
                WHERE is_active
                  AND pg_catalog.has_function_privilege('public.app_abrir_caja(bigint,bigint)', 'EXECUTE')
                  AND pg_catalog.has_function_privilege('public.app_cerrar_caja(bigint,bigint)', 'EXECUTE')
                  AND pg_catalog.has_function_privilege(
                      'public.app_registrar_venta(bigint,bigint,bigint,text,bigint,uuid,jsonb)', 'EXECUTE');
                """;
            var terminal = await connection.QuerySingleOrDefaultAsync<WorkstationInfo>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return terminal is null
                ? Result<WorkstationInfo>.Failure(new Error(ErrorCode.Authorization, "El puesto no está autorizado para operar."))
                : Result<WorkstationInfo>.Success(terminal);
        }, cancellationToken);

    internal Task<Result<WorkstationLicenseRow>> ReadLicenseAsync(CancellationToken cancellationToken) =>
        QueryAsync(async connection =>
        {
            const string sql = """
                SELECT signed_payload AS SignedPayload, workstation_slot AS Slot
                FROM public.app_licencia_puesto();
                """;
            var row = await connection.QuerySingleOrDefaultAsync<WorkstationLicenseRow>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return row is null
                ? Result<WorkstationLicenseRow>.Failure(new Error(
                    ErrorCode.LicenseBlocked, "El servidor no tiene una licencia disponible para este puesto."))
                : Result<WorkstationLicenseRow>.Success(row);
        }, cancellationToken);

    public Task<Result<IReadOnlyList<ActiveUserDto>>> ListUsersAsync(CancellationToken cancellationToken) =>
        QueryAsync(async connection =>
        {
            const string sql = """
                SELECT user_id::integer AS UserId, display_name AS DisplayName, user_role AS Role
                FROM public.app_user_directory() ORDER BY display_name;
                """;
            var users = await connection.QueryAsync<ActiveUserRow>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return Result<IReadOnlyList<ActiveUserDto>>.Success(
                users.Select(user => new ActiveUserDto(user.UserId, user.DisplayName, ToUserRole(user.Role))).ToArray());
        }, cancellationToken);

    public Task<Result<SignedInUser>> AuthenticateAsync(int userId, string pin, CancellationToken cancellationToken) =>
        QueryAsync(async connection =>
        {
            AdminToken = null;
            const string challengeSql = """
                SELECT u.display_name AS DisplayName, u.user_role AS Role, p.pin_salt AS Salt,
                       p.pin_iterations AS Iterations, p.pin_algorithm_version AS AlgorithmVersion
                FROM public.terminal_info() t
                CROSS JOIN public.app_user_directory() u
                CROSS JOIN public.app_pin_challenge(@UserId) p
                WHERE t.is_active AND u.user_id = @UserId;
                """;
            var challenge = await connection.QuerySingleOrDefaultAsync<PinChallenge>(
                new CommandDefinition(challengeSql, new { UserId = (long)userId }, cancellationToken: cancellationToken));
            if (challenge is null || challenge.Salt.Length != Pbkdf2PinHasher.SaltSize ||
                challenge.Iterations <= 0 || challenge.AlgorithmVersion != Pbkdf2PinHasher.CurrentAlgorithmVersion)
            {
                return InvalidPin();
            }

            var candidate = Pbkdf2PinHasher.DeriveKey(pin, challenge.Salt, challenge.Iterations);
            try
            {
                var verifySql = challenge.Role == "ADMIN" ? """
                    SELECT authenticated AS Authenticated, wait_seconds AS WaitSeconds, admin_token AS AdminToken
                    FROM public.app_admin_login(@UserId, @Candidate);
                    """ : """
                    SELECT authenticated AS Authenticated, wait_seconds AS WaitSeconds, NULL::uuid AS AdminToken
                    FROM public.app_pin_verify(@UserId, @Candidate);
                    """;
                // A failed attempt is committed too, so the engine keeps its lockout state.
                var result = await connection.QuerySingleAsync<PinVerification>(new CommandDefinition(
                    verifySql, new { UserId = (long)userId, Candidate = candidate }, cancellationToken: cancellationToken));
                if (result.WaitSeconds > 0)
                {
                    return Result<SignedInUser>.Failure(new Error(
                        ErrorCode.Authentication, $"PIN bloqueado. Intente de nuevo en {result.WaitSeconds} segundos."));
                }
                if (!result.Authenticated)
                {
                    return InvalidPin();
                }

                if (challenge.Role == "ADMIN" && result.AdminToken is null)
                {
                    return InvalidPin();
                }

                AdminToken = result.AdminToken;
                return Result<SignedInUser>.Success(new SignedInUser(userId, challenge.DisplayName, ToUserRole(challenge.Role)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(candidate);
            }
        }, cancellationToken);

    private const string OwnOpenCashSql = """
        SELECT c.cash_session_id
        FROM public.terminal_info() t
        JOIN public.v_open_cash_totals c USING (terminal_id)
        WHERE t.is_active
        """;

    public Task<Result<WorkstationCashState>> GetCashAsync(CancellationToken cancellationToken) =>
        QueryAsync(async connection =>
        {
            var cashId = await connection.ExecuteScalarAsync<long?>(
                new CommandDefinition(OwnOpenCashSql, cancellationToken: cancellationToken));
            return Result<WorkstationCashState>.Success(new(cashId));
        }, cancellationToken);

    public Task<Result<long>> OpenCashAsync(int userId, long balanceCop, CancellationToken cancellationToken) =>
        QueryAsync(async connection =>
        {
            const string sql = "SELECT public.app_abrir_caja(@UserId, @Balance);";
            var cashId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                sql, new { UserId = (long)userId, Balance = balanceCop }, cancellationToken: cancellationToken));
            return cashId.HasValue
                ? Result<long>.Success(cashId.Value)
                : Result<long>.Failure(new Error(ErrorCode.Conflict, "Ya hay una caja abierta en este puesto."));
        }, cancellationToken);

    public Task<Result<WorkstationCashClosure>> CloseCashAsync(long cashId, int userId, CancellationToken cancellationToken) =>
        QueryAsync(async connection =>
        {
            const string sql = """
                SELECT cash_id AS CashId, opening_balance AS OpeningBalance, cash_sales AS CashSales,
                       transfer_sales AS TransferSales, discount_total AS DiscountTotal, net_sales AS NetSales,
                       expected_cash AS ExpectedCash, sale_count AS SaleCount
                FROM public.app_cerrar_caja(@CashId, @UserId);
                """;
            var closure = await connection.QuerySingleAsync<WorkstationCashClosure>(new CommandDefinition(
                sql, new { CashId = cashId, UserId = (long)userId }, cancellationToken: cancellationToken));
            return Result<WorkstationCashClosure>.Success(closure);
        }, cancellationToken, "No se pudo confirmar el cierre. Reintente el cierre para consultar el resultado de esta misma caja.");

    public Task<Result<ProductDto>> FindProductAsync(string plu, CancellationToken cancellationToken) =>
        QueryAsync(async connection =>
        {
            const string sql = """
                SELECT product_id::integer AS ProductId, plu AS Plu, name AS Name, unit AS Unit,
                       price_cop AS PriceCop, stock_milli AS StockMilli, unit_locked AS UnitLocked
                FROM public.product WHERE plu = @Plu AND is_active;
                """;
            var product = await connection.QuerySingleOrDefaultAsync<ActiveProductRow>(
                new CommandDefinition(sql, new { Plu = plu }, cancellationToken: cancellationToken));
            return product is null
                ? Result<ProductDto>.Failure(new Error(ErrorCode.NotFound, "No hay un producto activo con ese PLU."))
                : Result<ProductDto>.Success(new(product.ProductId, product.Plu, product.Name, ProductUnits.Parse(product.Unit),
                    product.PriceCop, product.StockMilli, true, product.UnitLocked));
        }, cancellationToken);

    public Task<Result<WorkstationSale>> RegisterSaleAsync(
        int userId, Guid requestId, FinalizeSaleRequest request, CancellationToken cancellationToken) =>
        QueryAsync(async connection =>
        {
            const string sql = """
                SELECT sale_id AS SaleId, sale_number AS SaleNumber, total_cop AS TotalCop
                FROM public.app_registrar_venta(
                """ + "(" + OwnOpenCashSql + "), " + """
                    @UserId, @DiscountCop, @PaymentMethod, @ReceivedCop, @RequestId, CAST(@Lines AS jsonb));
                """;
            var lines = JsonSerializer.Serialize(request.Lines.Select(line => new
            {
                product_id = line.ProductId,
                quantity_milli = line.QuantityMilli,
                unit_price_cop = line.UnitPriceCop,
                plu = line.Plu,
                name = line.Name,
                unit = ProductUnits.ToCode(line.Unit),
            }));
            var sale = await connection.QuerySingleAsync<WorkstationSale>(new CommandDefinition(
                sql, new
                {
                    UserId = (long)userId, request.DiscountCop, request.PaymentMethod, request.ReceivedCop,
                    RequestId = requestId, Lines = lines,
                }, cancellationToken: cancellationToken));
            return Result<WorkstationSale>.Success(sale);
        }, cancellationToken, "No se pudo confirmar el cobro. Revise el historial antes de continuar.");

    private async Task<Result<T>> QueryAsync<T>(
        Func<DbConnection, Task<Result<T>>> query, CancellationToken cancellationToken, string? unconfirmedMessage = null)
        where T : notnull
    {
        try
        {
            await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
            return await query(connection);
        }
        catch (DatabaseUnavailableException)
        {
            return Result<T>.Failure(new Error(ErrorCode.Connection, DatabaseUnavailableException.UserMessage));
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.InsufficientPrivilege or PostgresErrorCodes.InvalidPassword)
        {
            return Result<T>.Failure(new Error(ErrorCode.Authorization, "El puesto o el usuario no tiene permiso para esta operación."));
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.UniqueViolation)
        {
            return Result<T>.Failure(new Error(ErrorCode.Conflict, "Revise la caja abierta y los datos de la operación."));
        }
        catch (NpgsqlException ex) when (unconfirmedMessage is not null && ex is not PostgresException)
        {
            return Result<T>.Failure(new Error(ErrorCode.Storage, unconfirmedMessage));
        }
        catch (NpgsqlException)
        {
            return Result<T>.Failure(new Error(ErrorCode.Storage, "No se pudo completar la operación en el servidor."));
        }
    }

    private static Result<SignedInUser> InvalidPin() =>
        Result<SignedInUser>.Failure(new Error(ErrorCode.Authentication, "Usuario o PIN incorrecto."));

    private static UserRole ToUserRole(string role) => role == "ADMIN" ? UserRole.Admin : UserRole.Cashier;

    internal sealed record WorkstationLicenseRow(string SignedPayload, long Slot);
    private sealed record ActiveUserRow(int UserId, string DisplayName, string Role);
    private sealed record ActiveProductRow(int ProductId, string Plu, string Name, string Unit, long PriceCop, long StockMilli, bool UnitLocked);
    private sealed record PinChallenge(string DisplayName, string Role, byte[] Salt, int Iterations, int AlgorithmVersion);
    private sealed record PinVerification(bool Authenticated, int WaitSeconds, Guid? AdminToken);
}
