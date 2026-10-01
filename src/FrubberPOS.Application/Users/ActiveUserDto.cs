using FrubberPOS.Domain.Users;

namespace FrubberPOS.Application.Users;

/// <summary>
/// Login-screen row: no secrets, safe for presentation (RF-AUT-001).
/// </summary>
public sealed record ActiveUserDto(int UserId, string DisplayName, UserRole Role)
{
    /// <summary>Iniciales para el avatar circular (máx. 2 letras).</summary>
    public string Initials
    {
        get
        {
            var words = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var initials = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
            return initials.Length == 0 ? "?" : initials;
        }
    }
}
