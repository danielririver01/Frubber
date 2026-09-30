using FrubberPOS.Application.Products;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;

namespace FrubberPOS.Tests.Desktop;

public sealed class AdjustStockViewModelTests
{
    [Fact]
    public async Task Save_Valid_ClosesWithSavedTrue()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, admin).ExecuteAsync(
            new CreateProductRequest("V1", "Tomate", ProductUnit.Kg, 3000, 1500), CancellationToken.None);
        var adjust = new AdjustStockUseCase(bed.Products, bed.Units, admin);
        var viewModel = new AdjustStockViewModel(adjust);

        var detail = await new GetProductUseCase(bed.Products, bed.Units, admin)
            .ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);
        viewModel.Load(detail.Value.Product);

        Assert.Equal("Tomate (V1)", viewModel.ProductLabel);
        Assert.Equal("1.5", viewModel.CurrentStockText);
        Assert.Equal("1.5", viewModel.NewStockText);

        bool? closed = null;
        viewModel.RequestClose += (_, saved) => closed = saved;

        viewModel.NewStockText = "2,25";
        await viewModel.SaveAsync();

        Assert.True(closed);
        Assert.Equal(string.Empty, viewModel.ErrorMessage);
        var after = await new GetProductUseCase(bed.Products, bed.Units, admin)
            .ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);
        Assert.Equal(2250, after.Value.Product.StockMilli);
    }

    [Fact]
    public async Task Save_InvalidText_ShowsErrorAndStaysOpen()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, admin).ExecuteAsync(
            new CreateProductRequest("V2", "Papa", ProductUnit.Unit, 800, 0), CancellationToken.None);
        var viewModel = new AdjustStockViewModel(new AdjustStockUseCase(bed.Products, bed.Units, admin));
        var detail = await new GetProductUseCase(bed.Products, bed.Units, admin)
            .ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);
        viewModel.Load(detail.Value.Product);

        var closed = false;
        viewModel.RequestClose += (_, _) => closed = true;

        viewModel.NewStockText = "1.5";
        await viewModel.SaveAsync();

        Assert.False(closed);
        Assert.Contains("entero", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Save_NotificationFromServer_ShowsMessageAndStaysOpen()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var viewModel = new AdjustStockViewModel(new AdjustStockUseCase(bed.Products, bed.Units, admin));

        viewModel.Load(new ProductDto(999, "X9", "Fantasma", ProductUnit.Unit, 100, 0, true, false));
        viewModel.NewStockText = "5";

        var closed = false;
        viewModel.RequestClose += (_, _) => closed = true;
        await viewModel.SaveAsync();

        Assert.False(closed);
        Assert.Contains("no existe", viewModel.ErrorMessage);
    }
}
