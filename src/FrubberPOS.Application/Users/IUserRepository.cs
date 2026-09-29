using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Users;

/// <summary>
/// User persistence port. Specific to users — no generic CRUD (P-DD §8.2).
/// Every method receives the caller's <see cref="IUnitOfWork"/> so reads
/// and the last-admin re-check share one connection and one transaction.
/// Consumed by use cases, never by ViewModels.
/// </summary>
public interface IUserRepository
{
    Task<UserCredentialsDto?> FindCredentialsByIdAsync(IUnitOfWork unit, int userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActiveUserDto>> ListActiveAsync(IUnitOfWork unit, CancellationToken cancellationToken);

    Task<UserDetailDto?> FindByIdAsync(IUnitOfWork unit, int userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserDetailDto>> ListAllAsync(IUnitOfWork unit, CancellationToken cancellationToken);

    Task<bool> DisplayNameExistsAsync(IUnitOfWork unit, string displayName, int? excludeUserId, CancellationToken cancellationToken);

    Task<int> CreateAsync(
        IUnitOfWork unit,
        string displayName,
        Domain.Users.UserRole role,
        byte[] pinSalt,
        byte[] pinHash,
        int pinIterations,
        int pinAlgorithmVersion,
        CancellationToken cancellationToken);

    Task UpdateNameAndRoleAsync(IUnitOfWork unit, int userId, string displayName, Domain.Users.UserRole role, CancellationToken cancellationToken);

    Task UpdatePinAsync(
        IUnitOfWork unit,
        int userId,
        byte[] pinSalt,
        byte[] pinHash,
        int pinIterations,
        int pinAlgorithmVersion,
        CancellationToken cancellationToken);

    Task SetActiveAsync(IUnitOfWork unit, int userId, bool isActive, CancellationToken cancellationToken);

    Task DeleteAsync(IUnitOfWork unit, int userId, CancellationToken cancellationToken);

    Task<int> CountActiveAdminsAsync(IUnitOfWork unit, CancellationToken cancellationToken);

    Task<bool> HasHistoryAsync(IUnitOfWork unit, int userId, CancellationToken cancellationToken);
}
