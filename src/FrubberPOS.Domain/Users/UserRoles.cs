namespace FrubberPOS.Domain.Users;

/// <summary>
/// Mapping between <see cref="UserRole"/> and its persisted codes
/// (<c>ADMIN</c>/<c>CASHIER</c> per 001_Initial.sql).
/// </summary>
public static class UserRoles
{
    public static string ToCode(UserRole role) => role switch
    {
        UserRole.Admin => "ADMIN",
        UserRole.Cashier => "CASHIER",
        _ => throw new ArgumentOutOfRangeException(nameof(role), "Rol desconocido.")
    };

    public static UserRole Parse(string code) => code switch
    {
        "ADMIN" => UserRole.Admin,
        "CASHIER" => UserRole.Cashier,
        _ => throw new InvalidOperationException($"Código de rol desconocido en base de datos: '{code}'.")
    };
}
