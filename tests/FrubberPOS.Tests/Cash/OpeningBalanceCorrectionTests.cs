using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Cash;

/// <summary>
/// Opening-balance correction (JHO-61), regla acordada con el dueño:
/// solo ADMIN, caja abierta y SIN ventas; si ya vendió, cerrar y reabrir.
/// </summary>
public sealed class OpeningBalanceCorrectionTests
{
    private static CorrectOpeningBalanceUseCase Sut(ProductTestBed bed, FrubberPOS.Tests.TestDoubles.FakeCurrentSession? session = null) =>
        new(bed.Sessions, bed.Units, session ?? ProductTestBed.AdminSession());

    private static async Task OpenCashAsync(ProductTestBed bed, long balanceCop = 10_000)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (@UserId, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
            new { UserId = 1, Salt = new byte[16], Hash = new byte[32] });

        var opened = await new OpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate)
            .ExecuteAsync(new OpenCashSessionRequest(balanceCop), CancellationToken.None);
        Assert.True(opened.IsSuccess, opened.Error?.Message);
    }

    [Fact]
    public async Task OpenCashWithoutSales_Corrects()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await OpenCashAsync(bed);

        var result = await Sut(bed).ExecuteAsync(new CorrectOpeningBalanceRequest(50_000), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var open = await new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None);
        Assert.Equal(50_000, open.Value.Session!.OpeningBalanceCop);
    }

    [Fact]
    public async Task WithSales_IsRefused_AndBalanceUntouched()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await OpenCashAsync(bed);

        var created = await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new CreateProductRequest("M1", "Mango", ProductUnit.Kg, 6_000, 5_000), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var finalized = await new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate)
            .ExecuteAsync(
                new FinalizeSaleRequest([new DraftSaleLine(created.Value.ProductId, "M1", "Mango", ProductUnit.Kg, 6_000, 1_000)], 0, "CASH", 10_000),
                CancellationToken.None);
        Assert.True(finalized.IsSuccess, finalized.Error?.Message);

        var result = await Sut(bed).ExecuteAsync(new CorrectOpeningBalanceRequest(50_000), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Contains("ya tiene ventas", result.Error.Message);

        var open = await new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None);
        Assert.Equal(10_000, open.Value.Session!.OpeningBalanceCop);
    }

    [Fact]
    public async Task NoOpenSession_IsRefused()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
                + " VALUES (1, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
                new { Salt = new byte[16], Hash = new byte[32] });
        }

        var result = await Sut(bed).ExecuteAsync(new CorrectOpeningBalanceRequest(1_000), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
    }

    [Fact]
    public async Task Cashier_IsDenied()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await OpenCashAsync(bed);

        var result = await Sut(bed, ProductTestBed.CashierSession()).ExecuteAsync(
            new CorrectOpeningBalanceRequest(50_000), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
    }

    [Fact]
    public async Task Negative_IsRejected()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await OpenCashAsync(bed);

        var result = await Sut(bed).ExecuteAsync(new CorrectOpeningBalanceRequest(-1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
    }
}
