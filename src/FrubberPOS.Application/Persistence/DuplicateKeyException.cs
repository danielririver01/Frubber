namespace FrubberPOS.Application.Persistence;

/// <summary>
/// Raised by persistence when a unique constraint or unique index rejects a
/// write (PostgreSQL SQLSTATE 23505). Use cases turn it into a business
/// <c>Conflict</c>, which is how the single-open cash invariant survives a
/// race between two openers (RN-CAJ-001): the pre-check alone has a window
/// and the unique index is the backstop. Exists so Application never has to
/// reference Npgsql (ADR-001 layering).
/// </summary>
public sealed class DuplicateKeyException : Exception
{
    public DuplicateKeyException(Exception innerException)
        : base("Ya existe un registro con los mismos valores únicos.", innerException)
    {
    }
}
