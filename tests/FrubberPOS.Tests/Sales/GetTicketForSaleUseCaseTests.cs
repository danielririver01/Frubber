using Dapper;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// Read path for the ticket (JHO-45): the document comes from the
/// historical sale rows and the business settings. Renaming the product
/// after the sale must not change the ticket (snapshot rule).
/// </summary>
public sealed class GetTicketForSaleUseCaseTests
{
    private static async Task SeedUserAndOpenCashAsync(ProductTestBed bed)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (@UserId, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
            new { UserId = 1, Salt = new byte[16], Hash = new byte[32] });
        await connection.ExecuteAsync(
            "INSERT INTO CashSession (OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (1, 0, 1, 'Admin', 0);");
    }

    private static async Task<long> SellMangoAsync(ProductTestBed bed)
    {
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var created = await create.ExecuteAsync(
            new CreateProductRequest("M1", "Mango", ProductUnit.Kg, 6_000, 5_000), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);

        var finalize = new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock);
        var finalized = await finalize.ExecuteAsync(
            new FinalizeSaleRequest(
                [new DraftSaleLine(created.Value.ProductId, "M1", "Mango", ProductUnit.Kg, 6_000, 1_250)],
                0, "CASH", 10_000),
            CancellationToken.None);
        Assert.True(finalized.IsSuccess, finalized.Error?.Message);
        return created.Value.ProductId;
    }

    [Fact]
    public async Task SavedSale_ComposesTheTicketFromHistory()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedUserAndOpenCashAsync(bed);
        await SellMangoAsync(bed);

        var useCase = new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units);
        var result = await useCase.ExecuteAsync(new GetTicketForSaleRequest(1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var ticket = result.Value;
        Assert.Equal("V-000001", ticket.SaleNumberText);
        Assert.Contains(ticket.Lines, line => line.Contains("Cajero: Admin"));
        Assert.Contains(ticket.Lines, line => line.Contains("Mango"));
        // JHO-67 N4 (formato aprobado): 1,25 kg × $ 6.000/kg = $ 7.500 (RN-IMP-004, ADR-009).
        Assert.Contains(ticket.Lines, line =>
            line.Contains("1,25 kg × $ 6.000/kg = $ 7.500"));
        Assert.Contains(ticket.Lines, line => line.Contains("TOTAL") && line.Contains("$ 7.500"));
        Assert.Contains(ticket.Lines, line => line.Contains("Recibido") && line.Contains("$ 10.000"));
        Assert.Contains(ticket.Lines, line => line.Contains("Cambio") && line.Contains("$ 2.500"));
    }

    [Fact]
    public async Task UnknownSaleNumber_FailsAsNotFound()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var useCase = new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units);
        var result = await useCase.ExecuteAsync(new GetTicketForSaleRequest(99), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
        Assert.Contains("V-000099", result.Error.Message);
    }

    [Fact]
    public async Task InvalidSaleNumber_FailsValidation()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var useCase = new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units);
        var result = await useCase.ExecuteAsync(new GetTicketForSaleRequest(0), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
    }

    [Fact]
    public async Task ProductRenamedAfterSale_TicketKeepsTheHistoricalName()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedUserAndOpenCashAsync(bed);
        var productId = await SellMangoAsync(bed);

        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "UPDATE Product SET Name = 'Mango Tommy', UnitLocked = UnitLocked WHERE ProductId = @ProductId;",
                new { ProductId = productId });
        }

        var useCase = new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units);
        var result = await useCase.ExecuteAsync(new GetTicketForSaleRequest(1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains(result.Value.Lines, line => line.Contains("Mango"));
        Assert.DoesNotContain(result.Value.Lines, line => line.Contains("Tommy"));
    }

    [Fact]
    public async Task ConfiguredBusiness_AppearsOnTheTicket()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedUserAndOpenCashAsync(bed);
        await SellMangoAsync(bed);

        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "UPDATE BusinessSetting SET BusinessName = 'Frutas Dan', PrinterName = 'POS-58',"
                + " TicketFooterMessage = 'Gracias por su compra' WHERE BusinessSettingId = 1;");
        }

        var useCase = new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units);
        var result = await useCase.ExecuteAsync(new GetTicketForSaleRequest(1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains(result.Value.Lines, line => line.Contains("Frutas Dan"));
        Assert.Contains(result.Value.Lines, line => line.Contains("Gracias por su compra"));
        Assert.Equal("POS-58", result.Value.ConfiguredPrinterName);
    }
}
