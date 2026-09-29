using FrubberPOS.Domain.Users;

namespace FrubberPOS.Application.Authentication;

/// <summary>
/// In-memory authenticated identity (P-DD §10.4). Carries no PIN.
/// </summary>
public sealed record SignedInUser(int UserId, string DisplayName, UserRole Role);
