namespace FrubberPOS.Application.Common;

/// <summary>
/// Typed failure codes returned inside <see cref="Error"/>.
/// Expected business failures travel here; exceptions stay reserved
/// for unexpected technical faults (P-DD-003).
/// </summary>
public enum ErrorCode
{
    Validation,
    Authentication,
    Authorization,
    NotFound,
    Conflict,
    LicenseBlocked,
    Storage,
    Printing,
    Backup,
    Unexpected
}
