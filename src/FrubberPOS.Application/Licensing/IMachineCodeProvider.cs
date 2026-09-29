namespace FrubberPOS.Application.Licensing;

/// <summary>
/// Team fingerprint safe for UI and logs: the copyable machine code plus
/// redacted hardware tokens. Raw serials never cross this boundary
/// (ADR-013, JHO-15, JHO-25).
/// </summary>
public sealed record MachineFingerprint(string MachineCode, IReadOnlyList<string> RedactedIdentifiers);

/// <summary>
/// Reads the local team identity. Implementations live in Infrastructure
/// (JHO-25): WMI serials with the documented fallback, hashed into the
/// machine code before returning.
/// </summary>
public interface IMachineCodeProvider
{
    Task<MachineFingerprint> GetFingerprintAsync(CancellationToken cancellationToken);
}
