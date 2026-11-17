using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Users;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

public sealed class Postgres20UserRepository(Postgres20WorkstationStore store) : IUserRepository
{
    private readonly Postgres20AdminCommands commands = new(store);
    private const string Select =
        "SELECT user_id AS UserId, display_name AS DisplayName, role, is_active AS IsActive FROM public.app_admin_users(@Token)";

    public Task<UserCredentialsDto?> FindCredentialsByIdAsync(IUnitOfWork unit, int userId, CancellationToken ct) =>
        throw new UnauthorizedAccessException("El PIN se verifica en el servidor.");

    public async Task<IReadOnlyList<ActiveUserDto>> ListActiveAsync(IUnitOfWork unit, CancellationToken ct)
    {
        var rows = await ListAllAsync(unit, ct);
        return rows.Where(row => row.IsActive).Select(row => new ActiveUserDto(row.UserId, row.DisplayName, row.Role)).ToList();
    }

    public async Task<UserDetailDto?> FindByIdAsync(IUnitOfWork unit, int userId, CancellationToken ct) =>
        (await commands.QueryAsync(unit, Select + " WHERE user_id = @userId", new { userId },
            unit.Connection.QuerySingleOrDefaultAsync<Row>, ct))?.ToDto();

    public async Task<IReadOnlyList<UserDetailDto>> ListAllAsync(IUnitOfWork unit, CancellationToken ct)
    {
        var rows = await commands.QueryAsync(unit, Select + " ORDER BY is_active DESC, lower(display_name)",
            null, unit.Connection.QueryAsync<Row>, ct);
        return rows.Select(row => row.ToDto()).ToList();
    }

    public Task<bool> DisplayNameExistsAsync(IUnitOfWork unit, string displayName, int? excludeUserId, CancellationToken ct) =>
        commands.QueryAsync(unit, """
            SELECT EXISTS (SELECT FROM public.app_admin_users(@Token)
            WHERE lower(display_name) = lower(@displayName) AND user_id IS DISTINCT FROM @excludeUserId)
            """, new { displayName, excludeUserId }, unit.Connection.QuerySingleAsync<bool>, ct);

    public Task<int> CreateAsync(IUnitOfWork unit, string displayName, UserRole role, byte[] pinSalt,
        byte[] pinHash, int pinIterations, int pinAlgorithmVersion, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT public.app_admin_user_create(@Token, @displayName, @Role, @pinSalt, @pinHash, @pinIterations, @pinAlgorithmVersion)",
            new { displayName, Role = UserRoles.ToCode(role), pinSalt, pinHash, pinIterations, pinAlgorithmVersion },
            unit.Connection.QuerySingleAsync<int>, ct);

    public Task UpdateNameAndRoleAsync(IUnitOfWork unit, int userId, string displayName, UserRole role, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT public.app_admin_user_update(@Token, @userId, @displayName, @Role)",
            new { userId, displayName, Role = UserRoles.ToCode(role) }, unit.Connection.ExecuteAsync, ct);

    public Task UpdatePinAsync(IUnitOfWork unit, int userId, byte[] pinSalt, byte[] pinHash,
        int pinIterations, int pinAlgorithmVersion, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT public.app_admin_user_set_pin(@Token, @userId, @pinSalt, @pinHash, @pinIterations, @pinAlgorithmVersion)",
            new { userId, pinSalt, pinHash, pinIterations, pinAlgorithmVersion }, unit.Connection.ExecuteAsync, ct);

    public Task SetActiveAsync(IUnitOfWork unit, int userId, bool isActive, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT public.app_admin_user_set_active(@Token, @userId, @isActive)",
            new { userId, isActive }, unit.Connection.ExecuteAsync, ct);

    public Task DeleteAsync(IUnitOfWork unit, int userId, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT public.app_admin_user_delete(@Token, @userId)",
            new { userId }, unit.Connection.ExecuteAsync, ct);

    public Task<int> CountActiveAdminsAsync(IUnitOfWork unit, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT count(*)::integer FROM public.app_admin_users(@Token) WHERE is_active AND role = 'ADMIN'",
            null, unit.Connection.QuerySingleAsync<int>, ct);

    public Task<int> CountAllAsync(IUnitOfWork unit, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT count(*)::integer FROM public.app_admin_users(@Token)",
            null, unit.Connection.QuerySingleAsync<int>, ct);

    public Task<bool> HasHistoryAsync(IUnitOfWork unit, int userId, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT coalesce((SELECT has_history FROM public.app_admin_users(@Token) WHERE user_id = @userId), false)",
            new { userId }, unit.Connection.QuerySingleAsync<bool>, ct);

    private sealed record Row(int UserId, string DisplayName, string Role, bool IsActive)
    {
        public UserDetailDto ToDto() => new(UserId, DisplayName, UserRoles.Parse(Role), IsActive);
    }
}
