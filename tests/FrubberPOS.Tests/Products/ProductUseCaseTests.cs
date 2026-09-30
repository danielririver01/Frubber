using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Tests.Products;

public sealed class ProductUseCaseTests
{
    private static CreateProductUseCase Create(ProductTestBed bed, ICurrentSession session) =>
        new(bed.Products, bed.Units, session);

    [Fact]
    public async Task Create_Valid_CreatesActiveUnlockedProduct()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = Create(bed, ProductTestBed.AdminSession());

        var created = await create.ExecuteAsync(
            new CreateProductRequest("M1", "Mango", ProductUnit.Kg, 4500, 0), CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.True(created.Value.ProductId > 0);

        var get = new GetProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var detail = await get.ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);
        Assert.True(detail.IsSuccess);
        Assert.Equal("M1", detail.Value.Product.Plu);
        Assert.Equal("Mango", detail.Value.Product.Name);
        Assert.Equal(ProductUnit.Kg, detail.Value.Product.Unit);
        Assert.Equal(4500, detail.Value.Product.PriceCop);
        Assert.Equal(0, detail.Value.Product.StockMilli);
        Assert.True(detail.Value.Product.IsActive);
        Assert.False(detail.Value.Product.UnitLocked);
    }

    [Fact]
    public async Task Create_NormalizesPluToUppercaseTrimmed()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = Create(bed, ProductTestBed.AdminSession());

        var created = await create.ExecuteAsync(
            new CreateProductRequest("  ab12 ", "Aguacate", ProductUnit.Unit, 3000, 0), CancellationToken.None);

        Assert.True(created.IsSuccess);
        var get = new GetProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var detail = await get.ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);
        Assert.Equal("AB12", detail.Value.Product.Plu);
    }

    [Fact]
    public async Task Create_DuplicatePlu_Conflict()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = Create(bed, ProductTestBed.AdminSession());

        Assert.True((await create.ExecuteAsync(
            new CreateProductRequest("m1", "Mango", ProductUnit.Kg, 4500, 0), CancellationToken.None)).IsSuccess);
        var duplicate = await create.ExecuteAsync(
            new CreateProductRequest("M1", "Mango Criollo", ProductUnit.Kg, 5000, 0), CancellationToken.None);

        Assert.True(duplicate.IsFailure);
        Assert.Equal(ErrorCode.Conflict, duplicate.Error!.Code);
        Assert.Equal("Plu", duplicate.Error.Field);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("m-1")]
    [InlineData("m 1")]
    [InlineData("plu-con-más-de-32-caracteres-inválido")]
    public async Task Create_BadPlu_Validation(string plu)
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = Create(bed, ProductTestBed.AdminSession());

        var result = await create.ExecuteAsync(
            new CreateProductRequest(plu, "Mango", ProductUnit.Kg, 4500, 0), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Equal("Plu", result.Error.Field);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task Create_BadPrice_Validation(long price)
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = Create(bed, ProductTestBed.AdminSession());

        var result = await create.ExecuteAsync(
            new CreateProductRequest("P1", "Papa", ProductUnit.Kg, price, 0), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Equal("PriceCop", result.Error.Field);
    }

    [Fact]
    public async Task Create_UnitProductWithFractionalStock_Validation()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = Create(bed, ProductTestBed.AdminSession());

        var result = await create.ExecuteAsync(
            new CreateProductRequest("U1", "Limón", ProductUnit.Unit, 500, 1500), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Equal("StockMilli", result.Error.Field);
    }

    [Fact]
    public async Task Create_KgProductWithFractionalStock_LocksUnit()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = Create(bed, ProductTestBed.AdminSession());

        var created = await create.ExecuteAsync(
            new CreateProductRequest("K1", "Tomate", ProductUnit.Kg, 3200, 2500), CancellationToken.None);

        Assert.True(created.IsSuccess);
        var get = new GetProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var detail = await get.ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);
        Assert.Equal(2500, detail.Value.Product.StockMilli);
        Assert.True(detail.Value.Product.UnitLocked);
    }

    [Fact]
    public async Task Create_AsCashier_Authorization()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = Create(bed, ProductTestBed.CashierSession());

        var result = await create.ExecuteAsync(
            new CreateProductRequest("C1", "Cebolla", ProductUnit.Kg, 2000, 0), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
    }

    [Fact]
    public async Task Update_Valid_ChangesFieldsAndPlu()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await Create(bed, admin).ExecuteAsync(
            new CreateProductRequest("E1", "Espinaca", ProductUnit.Kg, 6000, 0), CancellationToken.None);

        var update = new UpdateProductUseCase(bed.Products, bed.Units, admin);
        var updated = await update.ExecuteAsync(
            new UpdateProductRequest(created.Value.ProductId, " e2 ", "Espinaca Baby", ProductUnit.Lb, 7000), CancellationToken.None);

        Assert.True(updated.IsSuccess);
        var get = new GetProductUseCase(bed.Products, bed.Units, admin);
        var detail = await get.ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);
        Assert.Equal("E2", detail.Value.Product.Plu);
        Assert.Equal("Espinaca Baby", detail.Value.Product.Name);
        Assert.Equal(ProductUnit.Lb, detail.Value.Product.Unit);
        Assert.Equal(7000, detail.Value.Product.PriceCop);
    }

    [Fact]
    public async Task Update_DuplicatePlu_Conflict()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var create = Create(bed, admin);
        Assert.True((await create.ExecuteAsync(new CreateProductRequest("A1", "A", ProductUnit.Kg, 1000, 0), CancellationToken.None)).IsSuccess);
        var second = await create.ExecuteAsync(new CreateProductRequest("A2", "B", ProductUnit.Kg, 1000, 0), CancellationToken.None);

        var update = new UpdateProductUseCase(bed.Products, bed.Units, admin);
        var result = await update.ExecuteAsync(
            new UpdateProductRequest(second.Value.ProductId, "A1", "B", ProductUnit.Kg, 1000), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal("Plu", result.Error.Field);
    }

    [Fact]
    public async Task Update_UnknownProduct_NotFound()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var update = new UpdateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());

        var result = await update.ExecuteAsync(
            new UpdateProductRequest(999, "X1", "X", ProductUnit.Kg, 1000), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Update_UnitChangeWithStock_Conflict()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await Create(bed, admin).ExecuteAsync(
            new CreateProductRequest("S1", "Sandía", ProductUnit.Kg, 4000, 3000), CancellationToken.None);

        var update = new UpdateProductUseCase(bed.Products, bed.Units, admin);
        var result = await update.ExecuteAsync(
            new UpdateProductRequest(created.Value.ProductId, "S1", "Sandía", ProductUnit.Unit, 4000), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal("Unit", result.Error.Field);
        Assert.Contains("existencias", result.Error.Message);
    }

    [Fact]
    public async Task Update_UnitChangeAfterSalesWithZeroStock_Conflict()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await Create(bed, admin).ExecuteAsync(
            new CreateProductRequest("V1", "Uva", ProductUnit.Kg, 8000, 0), CancellationToken.None);
        await bed.SeedSaleLineAsync(created.Value.ProductId, "V1", "Uva");

        var update = new UpdateProductUseCase(bed.Products, bed.Units, admin);
        var result = await update.ExecuteAsync(
            new UpdateProductRequest(created.Value.ProductId, "V1", "Uva", ProductUnit.Lb, 8000), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal("Unit", result.Error.Field);
        Assert.Contains("ventas", result.Error.Message);
    }

    [Fact]
    public async Task Update_SameUnitWithStock_Ok()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await Create(bed, admin).ExecuteAsync(
            new CreateProductRequest("S2", "Tomate Chonto", ProductUnit.Kg, 3000, 1500), CancellationToken.None);

        var update = new UpdateProductUseCase(bed.Products, bed.Units, admin);
        var result = await update.ExecuteAsync(
            new UpdateProductRequest(created.Value.ProductId, "S2", "Tomate Chonto", ProductUnit.Kg, 3500), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Search_ByPluPrefixAndByNameCaseInsensitive()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var create = Create(bed, admin);
        await create.ExecuteAsync(new CreateProductRequest("M1", "Mango Tommy", ProductUnit.Kg, 4500, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("M2", "Mango Azúcar", ProductUnit.Kg, 4000, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("B1", "Banano", ProductUnit.Unit, 500, 0), CancellationToken.None);

        var search = new SearchProductsUseCase(bed.Products, bed.Units, ProductTestBed.CashierSession());

        var byPlu = await search.ExecuteAsync(new SearchProductsRequest("m", false), CancellationToken.None);
        Assert.True(byPlu.IsSuccess);
        Assert.Equal(2, byPlu.Value.Products.Count);

        var byName = await search.ExecuteAsync(new SearchProductsRequest("banano", false), CancellationToken.None);
        Assert.Single(byName.Value.Products);
        Assert.Equal("B1", byName.Value.Products[0].Plu);
    }

    [Fact]
    public async Task Search_EmptyQuery_ListsActiveOnlyUnlessAsked()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var create = Create(bed, admin);
        var first = await create.ExecuteAsync(new CreateProductRequest("L1", "Limón", ProductUnit.Unit, 500, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("L2", "Lulo", ProductUnit.Unit, 800, 0), CancellationToken.None);

        var deactivate = new SetProductActiveStateUseCase(bed.Products, bed.Units, admin);
        Assert.True((await deactivate.ExecuteAsync(
            new SetProductActiveStateRequest(first.Value.ProductId, false), CancellationToken.None)).IsSuccess);

        var search = new SearchProductsUseCase(bed.Products, bed.Units, ProductTestBed.CashierSession());
        var activeOnly = await search.ExecuteAsync(new SearchProductsRequest("", false), CancellationToken.None);
        Assert.Single(activeOnly.Value.Products);
        Assert.Equal("L2", activeOnly.Value.Products[0].Plu);

        var all = await search.ExecuteAsync(new SearchProductsRequest("", true), CancellationToken.None);
        Assert.Equal(2, all.Value.Products.Count);
        Assert.False(all.Value.Products.First(p => p.Plu == "L1").IsActive);
    }

    [Fact]
    public async Task Search_LikeWildcardsAreLiteral()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var create = Create(bed, admin);
        await create.ExecuteAsync(new CreateProductRequest("C1", "Coco 50% dto", ProductUnit.Unit, 3000, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("C2", "Coco entero", ProductUnit.Unit, 5000, 0), CancellationToken.None);

        var search = new SearchProductsUseCase(bed.Products, bed.Units, ProductTestBed.CashierSession());
        var result = await search.ExecuteAsync(new SearchProductsRequest("50%", false), CancellationToken.None);

        Assert.Single(result.Value.Products);
        Assert.Equal("C1", result.Value.Products[0].Plu);
    }

    [Fact]
    public async Task Search_WithoutSession_Authorization()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var search = new SearchProductsUseCase(bed.Products, bed.Units, new TestDoubles.FakeCurrentSession());

        var result = await search.ExecuteAsync(new SearchProductsRequest("", false), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
    }

    [Fact]
    public async Task SetActiveState_UnknownProduct_NotFound()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var setActive = new SetProductActiveStateUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());

        var result = await setActive.ExecuteAsync(new SetProductActiveStateRequest(999, false), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Delete_WithoutSales_RemovesProduct()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await Create(bed, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("D1", "Durazno", ProductUnit.Kg, 5500, 0), CancellationToken.None);

        var delete = new DeleteProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var deleted = await delete.ExecuteAsync(new DeleteProductRequest(created.Value.ProductId), CancellationToken.None);

        Assert.True(deleted.IsSuccess);
        var get = new GetProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var detail = await get.ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);
        Assert.Equal(ErrorCode.NotFound, detail.Error!.Code);
    }

    [Fact]
    public async Task Delete_WithSalesHistory_Conflict()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await Create(bed, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("H1", "Habanero", ProductUnit.Unit, 1000, 0), CancellationToken.None);
        await bed.SeedSaleLineAsync(created.Value.ProductId, "H1", "Habanero");

        var delete = new DeleteProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var result = await delete.ExecuteAsync(new DeleteProductRequest(created.Value.ProductId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Contains("Desactívelo", result.Error.Message);
    }

    [Fact]
    public async Task Delete_WithSalesHistory_ButCanStillDeactivate()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await Create(bed, admin).ExecuteAsync(
            new CreateProductRequest("H2", "Yuca", ProductUnit.Unit, 1200, 0), CancellationToken.None);
        await bed.SeedSaleLineAsync(created.Value.ProductId, "H2", "Yuca");

        var deactivate = new SetProductActiveStateUseCase(bed.Products, bed.Units, admin);
        var result = await deactivate.ExecuteAsync(
            new SetProductActiveStateRequest(created.Value.ProductId, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
