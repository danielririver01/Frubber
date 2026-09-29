using FrubberPOS.Domain.Users;

namespace FrubberPOS.Application.Users;

/// <summary>
/// Administration row: no secrets, safe for presentation (JHO-26).
/// </summary>
public sealed record UserDetailDto(int UserId, string DisplayName, UserRole Role, bool IsActive);
