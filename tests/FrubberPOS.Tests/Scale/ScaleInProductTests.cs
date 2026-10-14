using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Scale;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Scale;

/// <summary>
/// Scale inside the product screens (JHO-92): «Leer báscula» fills the
/// initial stock at creation and the new stock in the adjustment dialog —
/// the canasta/balde scenario. No tare is stored (the scale keeps its own
/// TARE key), the field stays editable and any failure keeps manual entry.
/// </summary>
public sealed class ScaleInProductTests
{
    private static async Task<ReadScaleWeightUseCase> EnableScaleAsync(
        ProductTestBed bed, IScaleReader? reader = null, string scaleUnit = "KG")
    {
        var admin = ProductTestBed.AdminSession();
        var update = new UpdateScaleSettingsUseCase(bed.ScaleSettings, bed.Units, admin);
        var saved = await update.ExecuteAsync(
            new UpdateScaleSettingsRequest(true, "COM3", 9600, ScaleProtocols.Continuous, scaleUnit, 1200),
            CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        return new ReadScaleWeightUseCase(bed.ScaleSettings, reader ?? new FakeScaleReader(), bed.Units, admin);
    }

    private static ProductEditorViewModel BuildEditor(ProductTestBed bed, ReadScaleWeightUseCase? readScale) =>
        new(
            new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new UpdateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            suggestPlu: null,
            readScale: readScale);

    [Fact]
    public async Task Create_KgProduct_ReadScaleFillsTheInitialStock()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var editor = BuildEditor(bed, await EnableScaleAsync(bed));
        editor.Load(null);

        Assert.True(editor.ScaleAvailable);
        await editor.ReadScaleAsync();

        Assert.Equal("1,25", editor.StockText);
        Assert.Equal("Peso tomado de la báscula.", editor.StatusMessage);
        Assert.Equal(string.Empty, editor.ErrorMessage);
    }

    [Fact]
    public async Task Create_ReadThenSave_StoresTheWeighedStock()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var editor = BuildEditor(bed, await EnableScaleAsync(bed));
        editor.Load(null);
        await editor.ReadScaleAsync();

        editor.Plu = "T1";
        editor.Name = "Tomate";
        editor.PriceText = "3000";
        bool? closed = null;
        editor.RequestClose += (_, saved) => closed = saved;
        await editor.SaveAsync();

        Assert.True(closed);
        var created = await new GetProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new GetProductRequest(1), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.Equal(1_250, created.Value.Product.StockMilli);
    }

    [Fact]
    public async Task Create_UnitProduct_NeverOffersTheScale()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var reader = new FakeScaleReader();
        var editor = BuildEditor(bed, await EnableScaleAsync(bed, reader));
        editor.Load(null);

        editor.UnitSelection = ProductUnit.Unit;
        Assert.False(editor.ScaleAvailable);

        editor.StockText = "12";
        await editor.ReadScaleAsync();

        Assert.Equal("12", editor.StockText); // ni lo toca
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task Create_ScaleDisabled_KeepsWhatTheOperatorTyped()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var reader = new FakeScaleReader();
        var editor = BuildEditor(
            bed, new ReadScaleWeightUseCase(bed.ScaleSettings, reader, bed.Units, admin));
        editor.Load(null);

        editor.StockText = "7";
        await editor.ReadScaleAsync();

        Assert.Equal("7", editor.StockText);
        Assert.Equal(string.Empty, editor.StatusMessage);
        Assert.Contains("desactivada", editor.ErrorMessage);
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task Create_LbProduct_KgScale_ReportsMismatchWithoutConverting()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var reader = new FakeScaleReader();
        var editor = BuildEditor(bed, await EnableScaleAsync(bed, reader, scaleUnit: "KG"));
        editor.Load(null);

        editor.UnitSelection = ProductUnit.Lb;
        editor.StockText = "3";
        await editor.ReadScaleAsync();

        Assert.Equal("3", editor.StockText); // RN-UNI-005: nada de conversión kg↔lb
        Assert.Contains("KG", editor.ErrorMessage);
        Assert.Contains("LB", editor.ErrorMessage);
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task EditMode_TheScaleButtonDisappears()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new CreateProductRequest("M1", "Mango", ProductUnit.Kg, 6_000, 1_500), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var existing = await new GetProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);

        var reader = new FakeScaleReader();
        var editor = BuildEditor(bed, await EnableScaleAsync(bed, reader));
        editor.Load(existing.Value.Product);

        Assert.False(editor.ScaleAvailable); // la existencia no es editable al editar
        Assert.Equal("1,5", editor.StockText);
    }

    [Fact]
    public async Task Adjust_KgProduct_ReadScaleFillsTheNewStock_AndSaves()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, admin).ExecuteAsync(
            new CreateProductRequest("T2", "Tomate", ProductUnit.Kg, 3_000, 1_500), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);

        var vm = new AdjustStockViewModel(
            new AdjustStockUseCase(bed.Products, bed.Units, admin), await EnableScaleAsync(bed));
        var detail = await new GetProductUseCase(bed.Products, bed.Units, admin)
            .ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);
        vm.Load(detail.Value.Product);

        Assert.True(vm.ScaleAvailable);
        await vm.ReadScaleAsync();
        Assert.Equal("1,25", vm.NewStockText);

        await vm.SaveAsync();
        var after = await new GetProductUseCase(bed.Products, bed.Units, admin)
            .ExecuteAsync(new GetProductRequest(created.Value.ProductId), CancellationToken.None);
        Assert.Equal(1_250, after.Value.Product.StockMilli);
    }

    [Fact]
    public async Task Adjust_UnitProduct_NeverOffersTheScale()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var reader = new FakeScaleReader();
        var vm = new AdjustStockViewModel(
            new AdjustStockUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            await EnableScaleAsync(bed, reader));
        vm.Load(new ProductDto(999, "X9", "Caja", ProductUnit.Unit, 100, 12, true, false));

        Assert.False(vm.ScaleAvailable);

        vm.NewStockText = "20";
        await vm.ReadScaleAsync();

        Assert.Equal("20", vm.NewStockText);
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task Adjust_UnstableReading_KeepsTheManualValue()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var reader = new FakeScaleReader
        {
            NextReading = Result<ScaleReading>.Success(new ScaleReading(900, false)),
        };
        var vm = new AdjustStockViewModel(
            new AdjustStockUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            await EnableScaleAsync(bed, reader));
        vm.Load(new ProductDto(1, "T3", "Tomate", ProductUnit.Kg, 3_000, 1_500, true, false));

        vm.NewStockText = "4,5";
        await vm.ReadScaleAsync();

        Assert.Equal("4,5", vm.NewStockText);
        Assert.Contains("estable", vm.ErrorMessage);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    [Fact]
    public async Task WithoutScaleWiring_TheScreensStayExactlyAsBefore()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();

        // Sin caso de uso inyectado el botón ni aparece (pantalla como siempre).
        var editor = new ProductEditorViewModel(
            new CreateProductUseCase(bed.Products, bed.Units, admin),
            new UpdateProductUseCase(bed.Products, bed.Units, admin));
        editor.Load(null);
        editor.StockText = "2,5";
        Assert.False(editor.ScaleAvailable);

        await editor.ReadScaleAsync();
        Assert.Equal("2,5", editor.StockText);
        Assert.Equal(string.Empty, editor.ErrorMessage);
        Assert.Equal(string.Empty, editor.StatusMessage);

        // Báscula desactivada en Configuración: mensaje claro y valor intacto.
        var adjust = new AdjustStockViewModel(
            new AdjustStockUseCase(bed.Products, bed.Units, admin),
            new ReadScaleWeightUseCase(bed.ScaleSettings, new FakeScaleReader(), bed.Units, admin));
        adjust.Load(new ProductDto(1, "T4", "Tomate", ProductUnit.Kg, 3_000, 1_500, true, false));
        adjust.NewStockText = "9";
        await adjust.ReadScaleAsync();

        Assert.Equal("9", adjust.NewStockText);
        Assert.Contains("desactivada", adjust.ErrorMessage);
    }
}
