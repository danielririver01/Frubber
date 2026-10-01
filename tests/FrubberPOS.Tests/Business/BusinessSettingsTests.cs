using FrubberPOS.Application.Business;
using FrubberPOS.Application.Common;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Business;

/// <summary>
/// Business configuration (JHO-51, §15.1/15.2): fields and printer,
/// empty string means not configured, approved length limits, admin-only.
/// </summary>
public sealed class BusinessSettingsTests
{
    private static UpdateBusinessSettingsUseCase Update(ProductTestBed bed, FakeCurrentSession? session = null) =>
        new(bed.Settings, bed.Units, session ?? ProductTestBed.AdminSession());

    private static GetBusinessSettingsUseCase Get(ProductTestBed bed, FakeCurrentSession? session = null) =>
        new(bed.Settings, bed.Units, session ?? ProductTestBed.AdminSession());

    [Fact]
    public async Task Defaults_AreAllNull_UntilConfigured()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await Get(bed).ExecuteAsync(new GetBusinessSettingsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(new BusinessSettingSnapshot(null, null, null, null, null, null), result.Value);
    }

    [Fact]
    public async Task Save_PersistsAllFields()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var saved = await Update(bed).ExecuteAsync(
            new UpdateBusinessSettingsRequest(
                "Frutas Don José", "900.123.456-1", "310 555 1234", "Cra 10 # 20-30",
                "¡Gracias por su compra!", "POS-58"),
            CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error?.Message);

        var read = await Get(bed).ExecuteAsync(new GetBusinessSettingsRequest(), CancellationToken.None);
        Assert.True(read.IsSuccess, read.Error?.Message);
        Assert.Equal(
            new BusinessSettingSnapshot(
                "Frutas Don José", "900.123.456-1", "310 555 1234", "Cra 10 # 20-30",
                "¡Gracias por su compra!", "POS-58"),
            read.Value);
    }

    [Fact]
    public async Task EmptyStrings_MeanNotConfigured()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await Update(bed).ExecuteAsync(
            new UpdateBusinessSettingsRequest("Frutas Don José", null, null, null, null, "POS-58"),
            CancellationToken.None);

        var result = await Update(bed).ExecuteAsync(
            new UpdateBusinessSettingsRequest("  ", "", "  ", null, "", ""),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var read = await Get(bed).ExecuteAsync(new GetBusinessSettingsRequest(), CancellationToken.None);
        Assert.Equal(new BusinessSettingSnapshot(null, null, null, null, null, null), read.Value);
    }

    [Theory]
    [InlineData(121)]  // nombre: máximo 120
    [InlineData(500)]
    public async Task OverLimit_IsRejected_WithTheApprovedLimit(int length)
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await Update(bed).ExecuteAsync(
            new UpdateBusinessSettingsRequest(new string('x', length), null, null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Contains("no puede superar 120 caracteres", result.Error.Message);

        // Nada quedó guardado.
        var read = await Get(bed).ExecuteAsync(new GetBusinessSettingsRequest(), CancellationToken.None);
        Assert.Null(read.Value.BusinessName);
    }

    [Fact]
    public async Task Cashier_IsDeniedForReadAndWrite()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var cashier = ProductTestBed.CashierSession();

        var read = await Get(bed, cashier).ExecuteAsync(new GetBusinessSettingsRequest(), CancellationToken.None);
        var write = await Update(bed, cashier).ExecuteAsync(
            new UpdateBusinessSettingsRequest("X", null, null, null, null, null), CancellationToken.None);

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCode.Authorization, read.Error!.Code);
        Assert.True(write.IsFailure);
        Assert.Equal(ErrorCode.Authorization, write.Error!.Code);
    }

    [Fact]
    public async Task ViewModel_LoadsSaves_AndHandlesNoPrinter()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var vm = new BusinessSettingsViewModel(Get(bed), Update(bed));

        await vm.InitializeAsync(["POS-58", "HP LaserJet"]);
        Assert.Equal("(Sin impresora configurada)", vm.SelectedPrinterOption);
        Assert.Equal(string.Empty, vm.BusinessNameText);

        vm.BusinessNameText = "Frutas Don José";
        vm.SelectedPrinterOption = "POS-58";
        await vm.SaveAsync();
        Assert.Equal("Configuración guardada.", vm.StatusMessage);

        var read = await Get(bed).ExecuteAsync(new GetBusinessSettingsRequest(), CancellationToken.None);
        Assert.Equal("Frutas Don José", read.Value.BusinessName);
        Assert.Equal("POS-58", read.Value.PrinterName);

        // Volver a "ninguna" guarda NULL (§15.2 permite no configurar).
        vm.SelectedPrinterOption = "(Sin impresora configurada)";
        await vm.SaveAsync();
        read = await Get(bed).ExecuteAsync(new GetBusinessSettingsRequest(), CancellationToken.None);
        Assert.Null(read.Value.PrinterName);
    }

    [Fact]
    public async Task ViewModel_UnknownConfiguredPrinter_FallsBackToNone()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await Update(bed).ExecuteAsync(
            new UpdateBusinessSettingsRequest(null, null, null, null, null, "Impresora vieja"), CancellationToken.None);

        var vm = new BusinessSettingsViewModel(Get(bed), Update(bed));
        await vm.InitializeAsync(["POS-58"]);

        Assert.Equal("(Sin impresora configurada)", vm.SelectedPrinterOption);
    }

    [Fact]
    public async Task ViewModel_ShowsLimitOnlyNearTheMaximum()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var vm = new BusinessSettingsViewModel(Get(bed), Update(bed));
        await vm.InitializeAsync([]);

        vm.BusinessNameText = new string('x', 100);
        Assert.Equal(string.Empty, vm.BusinessNameHintText);

        vm.BusinessNameText = new string('x', 115);
        Assert.Equal("115/120", vm.BusinessNameHintText);
    }
}
