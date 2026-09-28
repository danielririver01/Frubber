namespace FrubberPOS.Application.Common;

/// <summary>
/// User-safe failure description. Never carries PIN, salts, hashes,
/// full license keys, decrypted DPAPI payloads, full hardware
/// identifiers or internal SQL (P-DD-003, JHO-15).
/// </summary>
public sealed record Error
{
    public Error(ErrorCode code, string message, string? field = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("El mensaje de error no puede estar vacío.", nameof(message));
        }

        Code = code;
        Message = message;
        Field = field;
    }

    public ErrorCode Code { get; }

    public string Message { get; }

    public string? Field { get; }
}
