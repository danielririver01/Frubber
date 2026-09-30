using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Tests.Products;

public sealed class AdjustStockUseCaseTests
{
    [Fact]
    public async Task Adjust_ValidValue_ReflectedImmediatelyInSearch()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, admin).ExecuteAsync(
            new CreateProductRequest("A1", "Aguacate", ProductUnit.Kg, 9000, 0), CancellationToken.None);

        var adjust = new AdjustStockUseCase(bed.Products, bed.Units, admin);
        var adjusted = await adjust.ExecuteAsync(
            new AdjustStockRequest(created.Value.ProductId, 3500), CancellationToken.None);

        Assert.True(adjusted.IsSuccess);
        Assert.Equal(3500, adjusted.Value.StockMilli);

        var search = new SearchProductsUseCase(bed.Products, bed.Units, ProductTestBed.CashierSession());
        var found = await search.ExecuteAsync(new SearchProductsRequest("aguacate", ProductStatusFilter.Activos), CancellationToken.None);
        Assert.Single(found.Value.Products);
        Assert.Equal(3500, found.Value.Products[0].StockMilli);
    }

    [Fact]
    public async Task Adjust_NegativeStock_Allowed()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, admin).ExecuteAsync(
            new CreateProductRequest("N1", "Naranja", ProductUnit.Kg, 3000, 0), CancellationToken.None);

        var adjust = new AdjustStockUseCase(bed.Products, bed.Units, admin);
        var adjusted = await adjust.ExecuteAsync(
            new AdjustStockRequest(created.Value.ProductId, -750), CancellationToken.None);

        Assert.True(adjusted.IsSuccess);
        Assert.Equal(-750, adjusted.Value.StockMilli);
    }

    [Fact]
    public async Task Adjust_UnitProduct_FractionalValue_Validation()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, admin).ExecuteAsync(
            new CreateProductRequest("U1", "Limón", ProductUnit.Unit, 500, 0), CancellationToken.None);

        var adjust = new AdjustStockUseCase(bed.Products, bed.Units, admin);
        var result = await adjust.ExecuteAsync(
            new AdjustStockRequest(created.Value.ProductId, 2500 + 1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Equal("NewStockMilli", result.Error.Field);
    }

    [Fact]
    public async Task Adjust_UnitProduct_NegativeWhole_Ok()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, admin).ExecuteAsync(
            new CreateProductRequest("U2", "Mandarina", ProductUnit.Unit, 400, 0), CancellationToken.None);

        var adjust = new AdjustStockUseCase(bed.Products, bed.Units, admin);
        var adjusted = await adjust.ExecuteAsync(
            new AdjustStockRequest(created.Value.ProductId, -2000), CancellationToken.None);

        Assert.True(adjusted.IsSuccess);
        Assert.Equal(-2000, adjusted.Value.StockMilli);
    }

    [Fact]
    public async Task Adjust_FromZero_LocksUnit_AndZeroingNeverUnlocks()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, admin).ExecuteAsync(
            new CreateProductRequest("L1", "Lechuga", ProductUnit.Kg, 5000, 0), CancellationToken.None);

        var adjust = new AdjustStockUseCase(bed.Products, bed.Units, admin);
        var get = new GetProductUseCase(bed.Products, bed.Units, admin);

        Assert.True((await adjust.ExecuteAsync(new AdjustStockRequest(created.Value.ProductId, 1000), CancellationToken.None)).IsSuccess);
        var locked = await get.ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);
        Assert.True(locked.Value.Product.UnitLocked);

        // A unit change is now rejected even though the lock came from an adjustment (JHO-31: movements lock).
        var update = new UpdateProductUseCase(bed.Products, bed.Units, admin);
        var change = await update.ExecuteAsync(
            new UpdateProductRequest(created.Value.ProductId, "L1", "Lechuga", ProductUnit.Unit, 5000), CancellationToken.None);
        Assert.Equal(ErrorCode.Conflict, change.Error!.Code);

        Assert.True((await adjust.ExecuteAsync(new AdjustStockRequest(created.Value.ProductId, 0), CancellationToken.None)).IsSuccess);
        var stillLocked = await get.ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);
        Assert.True(stillLocked.Value.Product.UnitLocked);

        // Aun con existencia en cero, el movimiento anterior mantiene la unidad bloqueada.
        var changeAfterZero = await update.ExecuteAsync(
            new UpdateProductRequest(created.Value.ProductId, "L1", "Lechuga", ProductUnit.Unit, 5000), CancellationToken.None);
        Assert.Equal(ErrorCode.Conflict, changeAfterZero.Error!.Code);
        Assert.Contains("movimientos", changeAfterZero.Error.Message);
    }

    [Fact]
    public async Task Adjust_UnknownProduct_NotFound()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var adjust = new AdjustStockUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());

        var result = await adjust.ExecuteAsync(new AdjustStockRequest(999, 1000), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Adjust_AsCashier_Authorization()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("C1", "Cebolla", ProductUnit.Kg, 2000, 0), CancellationToken.None);

        var adjust = new AdjustStockUseCase(bed.Products, bed.Units, ProductTestBed.CashierSession());
        var result = await adjust.ExecuteAsync(
            new AdjustStockRequest(created.Value.ProductId, 1000), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
    }
}
