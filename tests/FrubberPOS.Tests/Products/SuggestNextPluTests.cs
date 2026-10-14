using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Products;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Tests.Products;

/// <summary>
/// JHO-87: automatic PLU suggestion when the product editor opens for a
/// NEW product (approved by the owner 2026-10-14). Covers the catalog-empty
/// start (1), the next number after mixed numeric/alphanumeric PLUs, the
/// admin-only gate, and the editor behavior (create vs edit vs typed PLU).
/// </summary>
public sealed class SuggestNextPluTests
{
    private static SuggestNextPluUseCase Suggest(ProductTestBed bed, ICurrentSession session) =>
        new(bed.Products, bed.Units, session);

    private static async Task SeedAsync(ProductTestBed bed, params string[] plus)
    {
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        foreach (var plu in plus)
        {
            var created = await create.ExecuteAsync(
                new CreateProductRequest(plu, $"Producto {plu}", ProductUnit.Kg, 1000, 0),
                CancellationToken.None);
            Assert.True(created.IsSuccess);
        }
    }

    [Fact]
    public async Task EmptyCatalog_SuggestsFirstNumber()
    {
        await using var bed = await ProductTestBed.CreateAsync();

        var next = await Suggest(bed, ProductTestBed.AdminSession())
            .ExecuteAsync(new SuggestNextPluRequest(), CancellationToken.None);

        Assert.True(next.IsSuccess);
        Assert.Equal("1", next.Value.Plu);
    }

    [Fact]
    public async Task MixedPlus_SuggestsMaxPlusOne()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedAsync(bed, "2", "5", "ABC");

        var next = await Suggest(bed, ProductTestBed.AdminSession())
            .ExecuteAsync(new SuggestNextPluRequest(), CancellationToken.None);

        // 'ABC' casts to 0 and never raises the suggestion: MAX(2,5,0)+1.
        Assert.True(next.IsSuccess);
        Assert.Equal("6", next.Value.Plu);
    }

    [Fact]
    public async Task Cashier_IsDenied()
    {
        await using var bed = await ProductTestBed.CreateAsync();

        var next = await Suggest(bed, ProductTestBed.CashierSession())
            .ExecuteAsync(new SuggestNextPluRequest(), CancellationToken.None);

        Assert.False(next.IsSuccess);
        Assert.Equal(ErrorCode.Authorization, next.Error!.Code);
    }

    [Fact]
    public async Task Editor_NewProduct_FillsSuggestedPlu()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedAsync(bed, "1", "2");

        var editor = new ProductEditorViewModel(
            new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new UpdateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            Suggest(bed, ProductTestBed.AdminSession()));
        editor.Load(null);
        await editor.SuggestNextPluAsync();

        Assert.Equal("3", editor.Plu);
    }

    [Fact]
    public async Task Editor_EditMode_KeepsExistingPlu()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedAsync(bed, "7");
        await using (var unit = await bed.Units.CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None))
        {
            var seeded = await bed.Products.SearchAsync(
                unit, "7", ProductStatusFilter.Activos, CancellationToken.None);
            var existing = Assert.Single(seeded);

            var editor = new ProductEditorViewModel(
                new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
                new UpdateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
                Suggest(bed, ProductTestBed.AdminSession()));
            editor.Load(existing);
            await editor.SuggestNextPluAsync();

            Assert.Equal("7", editor.Plu);
        }
    }

    [Fact]
    public async Task Editor_TypedPlu_IsNotOverwritten()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedAsync(bed, "1");

        var editor = new ProductEditorViewModel(
            new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new UpdateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            Suggest(bed, ProductTestBed.AdminSession()));
        editor.Load(null);
        editor.Plu = "999";
        await editor.SuggestNextPluAsync();

        Assert.Equal("999", editor.Plu);
    }
}
