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

    public GetUserUseCase(IUserRepository users, IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(units);
        this.users = users;
        this.units = units;
    }

    public async Task<Result<GetUserResponse>> ExecuteAsync(GetUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var user = await users.FindByIdAsync(unit, request.UserId, cancellationToken);

        if (user is null)
        {
            return Result<GetUserResponse>.Failure(new Error(ErrorCode.NotFound, "El usuario no existe."));
        }

        return Result<GetUserResponse>.Success(new GetUserResponse(user));
    }
}
