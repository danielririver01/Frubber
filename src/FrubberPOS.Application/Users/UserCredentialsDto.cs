using FrubberPOS.Domain.Users;

namespace FrubberPOS.Application.Users;

/// <summary>
/// Full account data for authentication. Carries PIN material, so it is
/// consumed only inside use cases — never returned to presentation.
/// </summary>
public sealed record UserCredentialsDto(
    int UserId,
    string DisplayName,
    UserRole Role,
    bool IsActive,
    byte[] PinSalt,
    byte[] PinHash,
    int PinIterations,
    int PinAlgorithmVersion);
