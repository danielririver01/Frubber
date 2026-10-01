using Dapper;
using FrubberPOS.Application.Business;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Infrastructure.Persistence.Business;

/// <summary>
/// Single-row business settings read over Dapper with parameterized SQL
/// (JHO-45). The initial migration seeds row 1 with all-null columns, so
/// every field stays optional until the configuration screen (JHO-51).
/// </summary>
public sealed class DapperBusinessSettingRepository : IBusinessSettingRepository
{
    public async Task<BusinessSettingSnapshot?> GetAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT BusinessName, TaxIdentifier, Phone, Address, TicketFooterMessage, PrinterName"
            + " FROM BusinessSetting WHERE BusinessSettingId = 1;";
        var row = await unit.Connection.QuerySingleOrDefaultAsync<BusinessSettingRow>(sql, transaction: unit.Transaction);
        return row is null
            ? null
            : new BusinessSettingSnapshot(
                row.BusinessName, row.TaxIdentifier, row.Phone, row.Address, row.TicketFooterMessage, row.PrinterName);
    }

    private sealed class BusinessSettingRow
    {
        public string? BusinessName { get; set; }

        public string? TaxIdentifier { get; set; }

        public string? Phone { get; set; }

        public string? Address { get; set; }

        public string? TicketFooterMessage { get; set; }

        public string? PrinterName { get; set; }
    }
}
