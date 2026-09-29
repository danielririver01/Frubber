using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Licensing;

/// <summary>
/// Single-row license state (table LicenseState, id always 1).
/// The payload is the DPAPI-sealed full license text, never plain.
/// </summary>
public sealed record StoredLicense(int PayloadVersion, byte[] ProtectedPayload, long UpdatedAtUtcMs);

/// <summary>
/// License persistence over the caller's unit of work (JHO-25).
/// </summary>
public interface ILicenseRepository
{
    Task<StoredLicense?> ReadAsync(IUnitOfWork unit, CancellationToken cancellationToken);

    Task SaveAsync(IUnitOfWork unit, StoredLicense license, CancellationToken cancellationToken);
}
