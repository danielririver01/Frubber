using FrubberPOS.Application.Business;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Sales;

public sealed record GetTicketForSaleRequest(long SaleNumber);

/// <summary>
/// Builds the printable ticket for a saved sale (JHO-45): reads the
/// historical snapshots from the sale tables plus the business settings
/// and composes the fixed-text document. Read-only: it never touches the
/// draft, the stock or the cobro — a failed or repeated ticket build can
/// never duplicate the sale (RN-IMP-001/005).
/// </summary>
public sealed class GetTicketForSaleUseCase : IUseCase<GetTicketForSaleRequest, TicketDocument>
{
    private readonly ISaleRepository sales;
    private readonly IBusinessSettingRepository settings;
    private readonly IUnitOfWorkFactory units;

    public GetTicketForSaleUseCase(
        ISaleRepository sales,
        IBusinessSettingRepository settings,
        IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(sales);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(units);
        this.sales = sales;
        this.settings = settings;
        this.units = units;
    }

    public async Task<Result<TicketDocument>> ExecuteAsync(GetTicketForSaleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SaleNumber <= 0)
        {
            return Result<TicketDocument>.Failure(new Error(ErrorCode.Validation, "El número de venta no es válido."));
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);

        var sale = await sales.FindSaleForTicketAsync(unit, request.SaleNumber, cancellationToken);
        if (sale is null)
        {
            return Result<TicketDocument>.Failure(new Error(ErrorCode.NotFound, $"No se encontró la venta V-{request.SaleNumber:D6}."));
        }

        var business = await settings.GetAsync(unit, cancellationToken);
        return Result<TicketDocument>.Success(TicketComposer.Compose(sale, business));
    }
}
