using FrubberPOS.Application.Common;
using FrubberPOS.Application.Scale;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Scale;

/// <summary>
/// Scale configuration and weighing (JHO-86): admin-only settings with the
/// approved lists, signed-in weighing, unit match (no kg↔lb conversion),
/// stability and failure paths keep manual entry available (RN-VEN-004).
/// </summary>
public sealed class ScaleUseCaseTests
{
    [Fact]
    public async Task GetScaleSettings_FreshDb_DefaultsToDisabled()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var get = new GetScaleSettingsUseCase(bed.ScaleSettings, bed.Units, ProductTestBed.AdminSession());

        var result = await get.ExecuteAsync(new GetScaleSettingsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.False(result.Value.IsEnabled);
        Assert.Null(result.Value.PortName);
        Assert.Equal(9600, result.Value.BaudRate);
        Assert.Equal(ScaleProtocols.Continuous, result.Value.Protocol);
        Assert.Equal("KG", result.Value.ScaleUnit);
    }

    [Fact]
    public async Task GetScaleSettings_AsCashier_Authorization()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var get = new GetScaleSettingsUseCase(bed.ScaleSettings, bed.Units, ProductTestBed.CashierSession());

        var result = await get.ExecuteAsync(new GetScaleSettingsRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
    }

    [Fact]
    public async Task Update_Valid_PersistsRoundTrip()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var update = new UpdateScaleSettingsUseCase(bed.ScaleSettings, bed.Units, admin);

        var saved = await update.ExecuteAsync(
            new UpdateScaleSettingsRequest(true, " COM3 ", 9600, ScaleProtocols.Torrey, "KG", 1500), CancellationToken.None);

        Assert.True(saved.IsSuccess, saved.Error?.Message);
        var get = new GetScaleSettingsUseCase(bed.ScaleSettings, bed.Units, admin);
        var loaded = await get.ExecuteAsync(new GetScaleSettingsRequest(), CancellationToken.None);
        Assert.True(loaded.Value.IsEnabled);
        Assert.Equal("COM3", loaded.Value.PortName); // se guarda sin espacios
        Assert.Equal(ScaleProtocols.Torrey, loaded.Value.Protocol);
        Assert.Equal(1500, loaded.Value.ReadTimeoutMs);
    }

    [Fact]
    public async Task Update_InvalidValues_Validation()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var update = new UpdateScaleSettingsUseCase(bed.ScaleSettings, bed.Units, ProductTestBed.AdminSession());

        Assert.Equal(ErrorCode.Validation, (await update.ExecuteAsync(
            new UpdateScaleSettingsRequest(true, "COM3", 12345, ScaleProtocols.Continuous, "KG", 1200), CancellationToken.None))
            .Error!.Code); // baudios fuera de la lista aprobada
        Assert.Equal(ErrorCode.Validation, (await update.ExecuteAsync(
            new UpdateScaleSettingsRequest(true, "COM3", 9600, "MORSE", "KG", 1200), CancellationToken.None))
            .Error!.Code); // protocolo desconocido
        Assert.Equal(ErrorCode.Validation, (await update.ExecuteAsync(
            new UpdateScaleSettingsRequest(true, "COM3", 9600, ScaleProtocols.Continuous, "G", 1200), CancellationToken.None))
            .Error!.Code); // unidad no soportada
        Assert.Equal(ErrorCode.Validation, (await update.ExecuteAsync(
            new UpdateScaleSettingsRequest(true, "COM3", 9600, ScaleProtocols.Continuous, "KG", 50), CancellationToken.None))
            .Error!.Code); // espera demasiado corta
    }

    [Fact]
    public async Task Update_AsCashier_Authorization()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var update = new UpdateScaleSettingsUseCase(bed.ScaleSettings, bed.Units, ProductTestBed.CashierSession());

        var result = await update.ExecuteAsync(
            new UpdateScaleSettingsRequest(true, "COM3", 9600, ScaleProtocols.Continuous, "KG", 1200), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
    }

    [Fact]
    public async Task ReadScale_Success_ReturnsWeightInThousandths()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var update = new UpdateScaleSettingsUseCase(bed.ScaleSettings, bed.Units, admin);
        Assert.True((await update.ExecuteAsync(
            new UpdateScaleSettingsRequest(true, "COM3", 9600, ScaleProtocols.Continuous, "KG", 1200), CancellationToken.None)).IsSuccess);

        var reader = new FakeScaleReader { NextReading = Result<ScaleReading>.Success(new ScaleReading(1_250, true)) };
        var read = new ReadScaleWeightUseCase(bed.ScaleSettings, reader, bed.Units, admin);
        var result = await read.ExecuteAsync(new ReadScaleWeightRequest("KG"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1_250, result.Value.QuantityMilli); // 1,250 kg tal como lo muestra el visor
    }

    [Fact]
    public async Task ReadScale_KgScaleAgainstLbProduct_RefusesWithoutConverting()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var update = new UpdateScaleSettingsUseCase(bed.ScaleSettings, bed.Units, admin);
        Assert.True((await update.ExecuteAsync(
            new UpdateScaleSettingsRequest(true, "COM3", 9600, ScaleProtocols.Continuous, "KG", 1200), CancellationToken.None)).IsSuccess);

        // RN-UNI-005: el sistema no convierte kg y lb entre sí.
        var reader = new FakeScaleReader();
        var read = new ReadScaleWeightUseCase(bed.ScaleSettings, reader, bed.Units, admin);
        var result = await read.ExecuteAsync(new ReadScaleWeightRequest("LB"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Contains("KG", result.Error.Message);
        Assert.Contains("LB", result.Error.Message); // orienta sin convertir (RN-UNI-005)
        Assert.Equal(0, reader.ReadCount); // ni siquiera abrió el puerto
    }

    [Fact]
    public async Task ReadScale_DisabledOrNoPort_Conflict()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var reader = new FakeScaleReader();
        var read = new ReadScaleWeightUseCase(bed.ScaleSettings, reader, bed.Units, admin);

        var disabled = await read.ExecuteAsync(new ReadScaleWeightRequest("KG"), CancellationToken.None);
        Assert.True(disabled.IsFailure);
        Assert.Contains("desactivada", disabled.Error!.Message);
        Assert.Equal(0, reader.ReadCount);

        var update = new UpdateScaleSettingsUseCase(bed.ScaleSettings, bed.Units, admin);
        Assert.True((await update.ExecuteAsync(
            new UpdateScaleSettingsRequest(true, null, 9600, ScaleProtocols.Continuous, "KG", 1200), CancellationToken.None)).IsSuccess);
        var noPort = await read.ExecuteAsync(new ReadScaleWeightRequest("KG"), CancellationToken.None);
        Assert.True(noPort.IsFailure);
        Assert.Contains("puerto", noPort.Error!.Message);
    }

    [Fact]
    public async Task ReadScale_UnstableOrZero_IsRejected_ManualStaysAvailable()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var update = new UpdateScaleSettingsUseCase(bed.ScaleSettings, bed.Units, admin);
        Assert.True((await update.ExecuteAsync(
            new UpdateScaleSettingsRequest(true, "COM3", 9600, ScaleProtocols.Systel, "KG", 1200), CancellationToken.None)).IsSuccess);

        var reader = new FakeScaleReader { NextReading = Result<ScaleReading>.Success(new ScaleReading(500, false)) };
        var read = new ReadScaleWeightUseCase(bed.ScaleSettings, reader, bed.Units, admin);
        var unstable = await read.ExecuteAsync(new ReadScaleWeightRequest("KG"), CancellationToken.None);
        Assert.True(unstable.IsFailure);
        Assert.Contains("estable", unstable.Error!.Message);

        reader.NextReading = Result<ScaleReading>.Success(new ScaleReading(0, true));
        var zero = await read.ExecuteAsync(new ReadScaleWeightRequest("KG"), CancellationToken.None);
        Assert.True(zero.IsFailure);
        Assert.Contains("cero", zero.Error!.Message);
    }

    [Fact]
    public async Task ReadScale_HardwareFailure_ReachesTheUserAsCleanError()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var update = new UpdateScaleSettingsUseCase(bed.ScaleSettings, bed.Units, admin);
        Assert.True((await update.ExecuteAsync(
            new UpdateScaleSettingsRequest(true, "COM3", 9600, ScaleProtocols.Continuous, "KG", 1200), CancellationToken.None)).IsSuccess);

        var reader = new FakeScaleReader
        {
            NextReading = Result<ScaleReading>.Failure(new Error(ErrorCode.Storage, "La báscula no respondió. Revise el cable.")),
        };
        var read = new ReadScaleWeightUseCase(bed.ScaleSettings, reader, bed.Units, admin);
        var result = await read.ExecuteAsync(new ReadScaleWeightRequest("KG"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Storage, result.Error!.Code);
    }

    [Fact]
    public async Task ReadScale_WithDraft_DoesNotTouchTheSavedConfiguration()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var reader = new FakeScaleReader();
        var read = new ReadScaleWeightUseCase(bed.ScaleSettings, reader, bed.Units, admin);

        // Prueba de conexión desde Configuración: usa los valores del formulario,
        // aunque la báscula siga desactivada en la base.
        var draft = new ScaleSnapshot(true, "COM5", 4800, ScaleProtocols.Cas, "LB", 800);
        var result = await read.ExecuteAsync(new ReadScaleWeightRequest("LB", draft), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("COM5", reader.LastSettings!.PortName);
        var get = new GetScaleSettingsUseCase(bed.ScaleSettings, bed.Units, admin);
        Assert.False((await get.ExecuteAsync(new GetScaleSettingsRequest(), CancellationToken.None)).Value.IsEnabled);
    }
}
