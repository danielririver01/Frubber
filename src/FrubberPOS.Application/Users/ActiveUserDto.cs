using FrubberPOS.Domain.Users;

namespace FrubberPOS.Application.Users;

/// <summary>
/// Login-screen row: no secrets, safe for presentation (RF-AUT-001).
/// </summary>
public sealed record ActiveUserDto(int UserId, string DisplayName, UserRole Role);
