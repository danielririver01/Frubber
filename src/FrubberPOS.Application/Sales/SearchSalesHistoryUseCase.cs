using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Sales;

/// <summary>History query (JHO-47): local calendar dates, 1-based page.</summary>
public sealed record SearchSalesHistoryRequest(DateOnly? FromDate, DateOnly? ToDate, int Page);

/// <summary>Paginated response; Page echoes the requested (valid) page.</summary>
public sealed record SearchSalesHistoryResponse(
    IReadOnlyList<SaleHistoryItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// Paginated sale history by date range (JHO-47, RN-HIS-001/004):
/// administrator-only, both dates required, Hasta never before Desde,
/// newest first, exactly 50 rows per page except the last. The local
/// calendar dates convert to a half-open UTC range with the machine zone
/// (single-store Windows POS, no DST in Colombia).
/// </summary>
public sealed class SearchSalesHistoryUseCase : IUseCase<SearchSalesHistoryRequest, SearchSalesHistoryResponse>
{
    public const int PageSize = 50;

    private readonly ISaleHistoryRepository history;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public SearchSalesHistoryUseCase(
        ISaleHistoryRepository history,
        IUnitOfWorkFactory units,
        ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.history = history;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<SearchSalesHistoryResponse>> ExecuteAsync(
        SearchSalesHistoryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<SearchSalesHistoryResponse>.Failure(denied);
        }

        if (request.FromDate is null || request.ToDate is null)
        {
            return Result<SearchSalesHistoryResponse>.Failure(new Error(
                ErrorCode.Validation, "Las fechas Desde y Hasta son obligatorias."));
        }

        if (request.ToDate.Value < request.FromDate.Value)
        {
            return Result<SearchSalesHistoryResponse>.Failure(new Error(
                ErrorCode.Validation, "Hasta no puede ser anterior a Desde."));
        }

        if (request.Page < 1)
        {
            return Result<SearchSalesHistoryResponse>.Failure(new Error(
                ErrorCode.Validation, "La página debe ser 1 o mayor."));
        }

        var zone = TimeZoneInfo.Local;
        var fromLocal = request.FromDate.Value.ToDateTime(TimeOnly.MinValue);
        var toExclusiveLocal = request.ToDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var fromUtcMs = new DateTimeOffset(fromLocal, zone.GetUtcOffset(fromLocal)).ToUnixTimeMilliseconds();
        var toUtcMsExclusive = new DateTimeOffset(toExclusiveLocal, zone.GetUtcOffset(toExclusiveLocal)).ToUnixTimeMilliseconds();

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var page = await history.SearchAsync(
            unit, fromUtcMs, toUtcMsExclusive, (request.Page - 1) * PageSize, PageSize, cancellationToken);

        return Result<SearchSalesHistoryResponse>.Success(
            new SearchSalesHistoryResponse(page.Items, page.TotalCount, request.Page, PageSize));
    }
}
