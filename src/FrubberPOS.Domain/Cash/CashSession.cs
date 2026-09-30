namespace FrubberPOS.Domain.Cash;

/// <summary>
/// Cash session (JHO-38, RN-CAJ-001…010): at most one open at a time
/// (enforced by UX_CashSession_OnlyOneOpen), opening balance in whole COP
/// starting at zero and never negative, opening records user and
/// timestamp. Closing asks for nothing (no counted cash, no reason,
/// RN-CAJ-008) and freezes the totals of the sales that were still valid
/// at that moment (RN-CAJ-010: later voids never rewrite them).
/// Rehydration comes straight from 001_Initial.sql rows.
/// </summary>
public sealed class CashSession
{
    public const int MaxNameLength = 60;

    private CashSession(
        int cashSessionId,
        long openedAtUtcMs,
        int openedAtOffsetMinutes,
        int openedByUserId,
        string openedByNameSnapshot,
        long openingBalanceCop)
    {
        CashSessionId = cashSessionId;
        OpenedAtUtcMs = openedAtUtcMs;
        OpenedAtOffsetMinutes = openedAtOffsetMinutes;
        OpenedByUserId = openedByUserId;
        OpenedByNameSnapshot = openedByNameSnapshot;
        OpeningBalanceCop = openingBalanceCop;
    }

    public int CashSessionId { get; }

    public long OpenedAtUtcMs { get; }

    public int OpenedAtOffsetMinutes { get; }

    public int OpenedByUserId { get; }

    public string OpenedByNameSnapshot { get; }

    public long OpeningBalanceCop { get; }

    /// <summary>Creates an open session; the timestamp comes from <c>IClock</c> in the use case.</summary>
    public static CashSession Open(
        long openedAtUtcMs,
        int openedAtOffsetMinutes,
        int openedByUserId,
        string openedByNameSnapshot,
        long openingBalanceCop)
    {
        if (openedAtUtcMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(openedAtUtcMs), "La fecha de apertura es obligatoria.");
        }

        if (openedAtOffsetMinutes is < -840 or > 840)
        {
            throw new ArgumentOutOfRangeException(nameof(openedAtOffsetMinutes), "La zona horaria de apertura es inválida.");
        }

        if (openedByUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(openedByUserId), "La apertura requiere un usuario.");
        }

        if (string.IsNullOrWhiteSpace(openedByNameSnapshot)
            || openedByNameSnapshot.Length > MaxNameLength
            || openedByNameSnapshot != openedByNameSnapshot.Trim())
        {
            throw new ArgumentException("El nombre del cajero debe tener entre 1 y 60 caracteres, sin espacios al inicio o fin.", nameof(openedByNameSnapshot));
        }

        if (openingBalanceCop < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(openingBalanceCop), "El saldo inicial no puede ser negativo.");
        }

        return new CashSession(0, openedAtUtcMs, openedAtOffsetMinutes, openedByUserId, openedByNameSnapshot, openingBalanceCop);
    }

    public static CashSession Rehydrate(
        int cashSessionId,
        long openedAtUtcMs,
        int openedAtOffsetMinutes,
        int openedByUserId,
        string openedByNameSnapshot,
        long openingBalanceCop) =>
        new(cashSessionId, openedAtUtcMs, openedAtOffsetMinutes, openedByUserId, openedByNameSnapshot, openingBalanceCop);
}
