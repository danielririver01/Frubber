using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Users;

namespace FrubberPOS.Application.Startup;

public sealed record CheckFirstStartRequest;

public sealed record CheckFirstStartResponse(bool IsFirstStart);

/// <summary>
/// Tells navigation whether to show the first-start wizard (no users) or
/// the normal login (JHO-28 backend for JHO-29).
/// </summary>
public sealed class CheckFirstStartUseCase : IUseCase<CheckFirstStartRequest, CheckFirstStartResponse>
{
    private readonly IUserRepository users;
    private readonly IUnitOfWorkFactory units;

    public CheckFirstStartUseCase(IUserRepository users, IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(units);
        this.users = users;
        this.units = units;
    }

    public async Task<Result<CheckFirstStartResponse>> ExecuteAsync(CheckFirstStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var count = await users.CountAllAsync(unit, cancellationToken);
        return Result<CheckFirstStartResponse>.Success(new CheckFirstStartResponse(count == 0));
    }
}
