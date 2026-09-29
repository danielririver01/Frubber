namespace FrubberPOS.Application.Persistence;

/// <summary>
/// Transaction scope of a unit of work (P-DD §8.2).
/// </summary>
public enum UnitOfWorkMode
{
    /// <summary>Read-only lists: no transaction.</summary>
    ReadOnly,

    /// <summary>Writes: deferred transaction, upgraded on first write.</summary>
    Write,

    /// <summary>
    /// Writes needing the write lock before reading and deciding
    /// (sale confirm, void, cash open/close): <c>BEGIN IMMEDIATE</c>.
    /// </summary>
    WriteImmediate
}
