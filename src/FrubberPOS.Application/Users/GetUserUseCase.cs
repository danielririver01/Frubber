using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Users;

public sealed record GetUserRequest(int UserId);

public sealed record GetUserResponse(UserDetailDto User);

/// <summary>
/// Administration detail, no secrets (JHO-26). Role authorization belongs
/// to JHO-29.
/// </summary>
public sealed class GetUserUseCase : IUseCase<GetUserRequest, GetUserResponse>
{
    private readonly IUserRepository users;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public GetUserUseCase(IUserRepository users, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.users = users;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<GetUserResponse>> ExecuteAsync(GetUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<GetUserResponse>.Failure(denied);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var user = await users.FindByIdAsync(unit, request.UserId, cancellationToken);

        if (user is null)
        {
            return Result<GetUserResponse>.Failure(new Error(ErrorCode.NotFound, "El usuario no existe."));
        }

        return Result<GetUserResponse>.Success(new GetUserResponse(user));
    }
}
