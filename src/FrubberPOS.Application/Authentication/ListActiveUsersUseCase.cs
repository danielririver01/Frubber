using FrubberPOS.Application.Common;
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

    public ListActiveUsersUseCase(IUserRepository users)
    {
        ArgumentNullException.ThrowIfNull(users);
        this.users = users;
    }

    public async Task<Result<ListActiveUsersResponse>> ExecuteAsync(ListActiveUsersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var active = await users.ListActiveAsync(cancellationToken);
        return Result<ListActiveUsersResponse>.Success(new ListActiveUsersResponse(active));
    }
}
