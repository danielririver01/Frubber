using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Cash;

public sealed class CashSessionUseCaseTests
{
    [Fact]
    public async Task Open_Valid_CreatesSingleOpenSession()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 30, 8, 30, 0, TimeSpan.FromHours(-5)));
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), clock, bed.Gate);

        var opened = await open.ExecuteAsync(new OpenCashSessionRequest(100_000), CancellationToken.None);

        Assert.True(opened.IsSuccess);
        var get = new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession());
        var current = await get.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None);
        Assert.NotNull(current.Value.Session);
        Assert.Equal(100_000, current.Value.Session.OpeningBalanceCop);
        Assert.Equal("Admin", current.Value.Session.OpenedByNameSnapshot);
        Assert.Equal(0, current.Value.Session.CashSalesCop);
        Assert.Equal(100_000, current.Value.Session.ExpectedCashCop);
    }

    [Fact]
    public async Task Open_AsCashier_Allowed()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.CashierSession(), bed.Clock, bed.Gate);

        // RN-CAJ-003: el cajero también puede abrir caja.
        var opened = await open.ExecuteAsync(new OpenCashSessionRequest(50_000), CancellationToken.None);

        Assert.True(opened.IsSuccess);
        var get = new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.CashierSession());
        var current = await get.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None);
        Assert.NotNull(current.Value.Session);
        Assert.Equal("Cajero", current.Value.Session.OpenedByNameSnapshot);
    }

    [Fact]
    public async Task Open_WhenGateBlocks_ReturnsLicenseBlocked()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);

        // P-DD §11.3 / RN-LIC-002: la apertura de caja se bloquea con la operación.
        bed.Gate.Blocked = new Error(ErrorCode.LicenseBlocked, "El periodo de prueba terminó. Active la licencia para continuar.");
        var result = await open.ExecuteAsync(new OpenCashSessionRequest(50_000), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.LicenseBlocked, result.Error!.Code);
        var get = new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession());
        Assert.Null((await get.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None)).Value.Session);
    }

    [Fact]
    public async Task Close_WhenGateBlocks_ReturnsLicenseBlocked()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        Assert.True((await open.ExecuteAsync(new OpenCashSessionRequest(50_000), CancellationToken.None)).IsSuccess);

        // P-DD §11.3 / RN-LIC-002: el cierre también valida licencia/reloj.
        bed.Gate.Blocked = new Error(ErrorCode.LicenseBlocked, "El periodo de prueba terminó. Active la licencia para continuar.");
        var close = new CloseCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        var result = await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.LicenseBlocked, result.Error!.Code);
        var get = new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession());
        Assert.NotNull((await get.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None)).Value.Session);
    }

    [Fact]
    public async Task Open_NegativeBalance_Validation()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);

        var result = await open.ExecuteAsync(new OpenCashSessionRequest(-1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
    }

    [Fact]
    public async Task Open_WhileOneOpen_Conflict()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);

        Assert.True((await open.ExecuteAsync(new OpenCashSessionRequest(0), CancellationToken.None)).IsSuccess);
        var second = await open.ExecuteAsync(new OpenCashSessionRequest(50_000), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorCode.Conflict, second.Error!.Code);
    }

    [Fact]
    public async Task Open_WithoutSession_Authorization()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, new FakeCurrentSession(), bed.Clock, bed.Gate);

        var result = await open.ExecuteAsync(new OpenCashSessionRequest(0), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
    }

    [Fact]
    public async Task GetOpen_WithNone_ReturnsNullSession()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var get = new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession());

        var result = await get.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Session);
    }

    [Fact]
    public async Task Close_OpenSession_FreezesZeroTotals()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        Assert.True((await open.ExecuteAsync(new OpenCashSessionRequest(75_000), CancellationToken.None)).IsSuccess);

        var close = new CloseCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        var closed = await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None);

        Assert.True(closed.IsSuccess);
        var get = new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession());
        Assert.Null((await get.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None)).Value.Session);
    }

    [Fact]
    public async Task Close_NothingOpen_Conflict()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var close = new CloseCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);

        var result = await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
    }

    [Fact]
    public async Task Close_Twice_Conflict()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        await open.ExecuteAsync(new OpenCashSessionRequest(0), CancellationToken.None);
        var close = new CloseCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        Assert.True((await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None)).IsSuccess);

        var again = await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None);

        Assert.True(again.IsFailure);
        Assert.Equal(ErrorCode.Conflict, again.Error!.Code);
    }

    [Fact]
    public async Task OpenAfterClose_Succeeds()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        var close = new CloseCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);

        await open.ExecuteAsync(new OpenCashSessionRequest(0), CancellationToken.None);
        await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None);
        var reopen = await open.ExecuteAsync(new OpenCashSessionRequest(10_000), CancellationToken.None);

        Assert.True(reopen.IsSuccess);
    }

    [Fact]
    public async Task Close_AsCashier_Allowed()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        await open.ExecuteAsync(new OpenCashSessionRequest(0), CancellationToken.None);

        var close = new CloseCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.CashierSession(), bed.Clock, bed.Gate);
        var result = await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
