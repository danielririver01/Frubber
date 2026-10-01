using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Business;

/// <summary>
/// Business data and printer configuration shown on the ticket
/// (RN-IMP-006): name, tax identifier, phone, address, footer message and
/// the configured Windows printer name. Every field may be null until the
/// operator configures it (the initial migration seeds one all-null row).
/// JHO-45 reads it; editing belongs to the configuration screen (JHO-51).
/// </summary>
public sealed record BusinessSettingSnapshot(
    string? BusinessName,
    string? TaxIdentifier,
    string? Phone,
    string? Address,
    string? TicketFooterMessage,
    string? PrinterName);

/// <summary>Business settings read port (JHO-45). Single-row table.</summary>
public interface IBusinessSettingRepository
{
    /// <summary>The single settings row; <c>null</c> only when missing.</summary>
    Task<BusinessSettingSnapshot?> GetAsync(IUnitOfWork unit, CancellationToken cancellationToken);
}
