using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Users;

namespace FrubberPOS.Application.Authentication;

public sealed record ListActiveUsersRequest;

public sealed record ListActiveUsersResponse(IReadOnlyList<ActiveUserDto> Users);

/// <summary>
/// Lists only active users for the login selector (RF-AUT-001). Rows carry
/// no secrets.
/// </summary>
public sealed class ListActiveUsersUseCase : IUseCase<ListActiveUsersRequest, ListActiveUsersResponse>
{
    private readonly IUserRepository users;
    private readonly IUnitOfWorkFactory units;

    public ListActiveUsersUseCase(IUserRepository users, IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(units);
        this.users = users;
        this.units = units;
    }

    public async Task<Result<ListActiveUsersResponse>> ExecuteAsync(ListActiveUsersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var active = await users.ListActiveAsync(unit, cancellationToken);
        return Result<ListActiveUsersResponse>.Success(new ListActiveUsersResponse(active));
    }
}
