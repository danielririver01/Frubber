using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Users;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Infrastructure.Persistence.Users;

/// <summary>
/// User persistence over Dapper with parameterized SQL (JHO-24, JHO-26).
/// Stateless: every method works on the caller's <see cref="IUnitOfWork"/>
/// so reads and the last-admin re-check share one connection and one
/// transaction. Friendly duplicate-name checks live in use cases; the
/// NOCASE unique index is the backstop.
/// </summary>
public sealed class DapperUserRepository : IUserRepository
{
    public async Task<UserCredentialsDto?> FindCredentialsByIdAsync(IUnitOfWork unit, int userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion"
            + " FROM \"User\" WHERE UserId = @UserId;";
        var row = await unit.Connection.QuerySingleOrDefaultAsync<UserRow>(sql, new { UserId = userId }, unit.Transaction);

        if (row is null)
        {
            return null;
        }

        return new UserCredentialsDto(
            row.UserId,
            row.DisplayName,
            UserRoles.Parse(row.Role),
            row.IsActive != 0,
            row.PinSalt,
            row.PinHash,
            row.PinIterations,
            row.PinAlgorithmVersion);
    }

    public async Task<IReadOnlyList<ActiveUserDto>> ListActiveAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT UserId, DisplayName, Role FROM \"User\""
            + " WHERE IsActive = 1 ORDER BY DisplayName COLLATE NOCASE;";
        var rows = await unit.Connection.QueryAsync<UserRow>(sql, transaction: unit.Transaction);

        return rows.Select(row => new ActiveUserDto(row.UserId, row.DisplayName, UserRoles.Parse(row.Role))).ToList();
    }

    public async Task<UserDetailDto?> FindByIdAsync(IUnitOfWork unit, int userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT UserId, DisplayName, Role, IsActive FROM \"User\" WHERE UserId = @UserId;";
        var row = await unit.Connection.QuerySingleOrDefaultAsync<UserRow>(sql, new { UserId = userId }, unit.Transaction);

        if (row is null)
        {
            return null;
        }

        return new UserDetailDto(row.UserId, row.DisplayName, UserRoles.Parse(row.Role), row.IsActive != 0);
    }

    public async Task<IReadOnlyList<UserDetailDto>> ListAllAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT UserId, DisplayName, Role, IsActive FROM \"User\""
            + " ORDER BY IsActive DESC, DisplayName COLLATE NOCASE;";
        var rows = await unit.Connection.QueryAsync<UserRow>(sql, transaction: unit.Transaction);

        return rows.Select(row => new UserDetailDto(row.UserId, row.DisplayName, UserRoles.Parse(row.Role), row.IsActive != 0)).ToList();
    }

    public async Task<bool> DisplayNameExistsAsync(IUnitOfWork unit, string displayName, int? excludeUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT COUNT(*) FROM \"User\""
            + " WHERE DisplayName = @Name COLLATE NOCASE AND (@Exclude IS NULL OR UserId <> @Exclude);";
        var count = await unit.Connection.QuerySingleAsync<long>(
            sql, new { Name = displayName, Exclude = (int?)excludeUserId }, unit.Transaction);
        return count > 0;
    }

    public async Task<int> CreateAsync(
        IUnitOfWork unit,
        string displayName,
        UserRole role,
        byte[] pinSalt,
        byte[] pinHash,
        int pinIterations,
        int pinAlgorithmVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "INSERT INTO \"User\" (DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (@Name, @Role, 1, @Salt, @Hash, @Iterations, @Version);"
            + "SELECT last_insert_rowid();";
        return await unit.Connection.QuerySingleAsync<int>(
            sql,
            new
            {
                Name = displayName,
                Role = UserRoles.ToCode(role),
                Salt = pinSalt,
                Hash = pinHash,
                Iterations = pinIterations,
                Version = pinAlgorithmVersion
            },
            unit.Transaction);
    }

    public async Task UpdateNameAndRoleAsync(IUnitOfWork unit, int userId, string displayName, UserRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "UPDATE \"User\" SET DisplayName = @Name, Role = @Role WHERE UserId = @UserId;";
        await unit.Connection.ExecuteAsync(
            sql, new { Name = displayName, Role = UserRoles.ToCode(role), UserId = userId }, unit.Transaction);
    }

    public async Task UpdatePinAsync(
        IUnitOfWork unit,
        int userId,
        byte[] pinSalt,
        byte[] pinHash,
        int pinIterations,
        int pinAlgorithmVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "UPDATE \"User\""
            + " SET PinSalt = @Salt, PinHash = @Hash, PinIterations = @Iterations, PinAlgorithmVersion = @Version"
            + " WHERE UserId = @UserId;";
        await unit.Connection.ExecuteAsync(
            sql,
            new { Salt = pinSalt, Hash = pinHash, Iterations = pinIterations, Version = pinAlgorithmVersion, UserId = userId },
            unit.Transaction);
    }

    public async Task SetActiveAsync(IUnitOfWork unit, int userId, bool isActive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "UPDATE \"User\" SET IsActive = @Active WHERE UserId = @UserId;";
        await unit.Connection.ExecuteAsync(sql, new { Active = isActive ? 1 : 0, UserId = userId }, unit.Transaction);
    }

    public async Task DeleteAsync(IUnitOfWork unit, int userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "DELETE FROM \"User\" WHERE UserId = @UserId;";
        await unit.Connection.ExecuteAsync(sql, new { UserId = userId }, unit.Transaction);
    }

    public async Task<int> CountActiveAdminsAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT COUNT(*) FROM \"User\" WHERE Role = 'ADMIN' AND IsActive = 1;";
        return await unit.Connection.QuerySingleAsync<int>(sql, transaction: unit.Transaction);
    }

    public async Task<bool> HasHistoryAsync(IUnitOfWork unit, int userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT CASE WHEN EXISTS (SELECT 1 FROM Sale WHERE CashierUserId = @UserId)"
            + " OR EXISTS (SELECT 1 FROM CashSession WHERE OpenedByUserId = @UserId OR ClosedByUserId = @UserId)"
            + " OR EXISTS (SELECT 1 FROM SaleVoid WHERE AdminUserId = @UserId) THEN 1 ELSE 0 END;";
        return await unit.Connection.QuerySingleAsync<int>(sql, new { UserId = userId }, unit.Transaction) != 0;
    }

    private sealed class UserRow
    {
        public int UserId { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public int IsActive { get; set; }

        public byte[] PinSalt { get; set; } = [];

        public byte[] PinHash { get; set; } = [];

        public int PinIterations { get; set; }

        public int PinAlgorithmVersion { get; set; }
    }
}
