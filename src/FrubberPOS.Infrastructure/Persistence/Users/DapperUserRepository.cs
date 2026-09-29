using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Users;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Infrastructure.Persistence.Users;

/// <summary>
/// User persistence over Dapper with parameterized SQL (JHO-24). Reads run
/// in their own read-only unit; write flows (JHO-26/JHO-28) will receive
/// the caller's unit to share connection and transaction.
/// </summary>
public sealed class DapperUserRepository : IUserRepository
{
    private readonly IUnitOfWorkFactory units;

    public DapperUserRepository(IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(units);
        this.units = units;
    }

    public async Task<UserCredentialsDto?> FindCredentialsByIdAsync(int userId, CancellationToken cancellationToken)
    {
        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);

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

    public async Task<IReadOnlyList<ActiveUserDto>> ListActiveAsync(CancellationToken cancellationToken)
    {
        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);

        const string sql = "SELECT UserId, DisplayName, Role FROM \"User\""
            + " WHERE IsActive = 1 ORDER BY DisplayName COLLATE NOCASE;";
        var rows = await unit.Connection.QueryAsync<UserRow>(sql, transaction: unit.Transaction);

        return rows.Select(row => new ActiveUserDto(row.UserId, row.DisplayName, UserRoles.Parse(row.Role))).ToList();
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
