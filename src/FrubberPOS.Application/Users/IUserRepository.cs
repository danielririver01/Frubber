namespace FrubberPOS.Application.Users;

/// <summary>
/// User persistence port. Specific to users — no generic CRUD (P-DD §8.1).
/// Methods are added per issue as features need them; repositories are
/// consumed by use cases, never by ViewModels.
/// </summary>
public interface IUserRepository
{
    Task<UserCredentialsDto?> FindCredentialsByIdAsync(int userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActiveUserDto>> ListActiveAsync(CancellationToken cancellationToken);
}
