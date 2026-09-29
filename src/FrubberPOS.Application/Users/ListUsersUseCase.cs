using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Users;

public sealed record ListUsersRequest;

public sealed record ListUsersResponse(IReadOnlyList<UserDetailDto> Users);

/// <summary>
/// Administration list: everyone including inactive, no secrets
/// (RF-USR-001). Role authorization belongs to JHO-29.
/// </summary>
public sealed class ListUsersUseCase : IUseCase<ListUsersRequest, ListUsersResponse>
{
    private readonly IUserRepository users;
    private readonly IUnitOfWorkFactory units;

    public ListUsersUseCase(IUserRepository users, IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(units);
        this.users = users;
        this.units = units;
    }

    public async Task<Result<ListUsersResponse>> ExecuteAsync(ListUsersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var all = await users.ListAllAsync(unit, cancellationToken);
        return Result<ListUsersResponse>.Success(new ListUsersResponse(all));
    }
}
