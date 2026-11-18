using Dapper;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestInfrastructure;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// JHO-139 / AUD-04: RN-VEN-010/011/015/016, RF-VEN-014/029/032.
/// Rejected charges preserve existing sales, lines, inventory, cash and numbering.
/// Each scenario starts with an actual sale, so preservation is checked on nonempty data.
/// </summary>
public sealed class FinalizeSaleValidationTests
{
    private const long PriceCop = 4_200;

    [Theory]
    [InlineData(0)]
    [InlineData(4_199)]
    public async Task Cash_ReceivedBelowTotal_RejectsWithoutChanges_ThenExactPaymentSucceeds(long receivedCop)
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var productId = await PrepareAsync(bed);
        var before = await SnapshotAsync();

        // RN-VEN-011 / RF-VEN-032: zero and one COP below total are insufficient.
        var rejected = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(productId)], 0, "CASH", receivedCop), CancellationToken.None);

        AssertRejected(rejected, ErrorCode.Validation, "el efectivo recibido no alcanza");
        Assert.Equal(before, await SnapshotAsync());

        // The inclusive boundary is valid; a rejected attempt consumes no number.
        var accepted = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(productId)], 0, "CASH", PriceCop), CancellationToken.None);
        Assert.True(accepted.IsSuccess, accepted.Error?.Message);
        Assert.Equal(2, accepted.Value.SaleNumber);
        Assert.Equal(PriceCop, accepted.Value.TotalCop);

        await using var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        Assert.Equal(PriceCop, await connection.ExecuteScalarAsync<long>(
            "SELECT ReceivedCop FROM Sale WHERE SaleNumber = 2;"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>(
            "SELECT ChangeCop FROM Sale WHERE SaleNumber = 2;"));
        Assert.Equal(-1_000, await connection.ExecuteScalarAsync<long>(
            "SELECT StockMilli FROM Product WHERE ProductId = @ProductId;", new { ProductId = productId }));
        Assert.Equal(58_400, await connection.ExecuteScalarAsync<long>(
            "SELECT ExpectedCashCop FROM V_OpenCashTotals;"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    public async Task Cash_MissingOrNegativeReceived_RejectsWithoutChanges(long? receivedCop)
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var productId = await PrepareAsync(bed);
        var before = await SnapshotAsync();

        // RF-VEN-014: CASH requires a received amount; null must not reach persistence.
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(productId)], 0, "CASH", receivedCop), CancellationToken.None);

        AssertRejected(result, ErrorCode.Validation, "Efectivo recibido es obligatorio");
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptySale_RejectsWithoutChanges(bool nullLines)
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await PrepareAsync(bed);
        var before = await SnapshotAsync();

        // RF-VEN-029: neither an empty list nor an absent list constitutes a sale.
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest(nullLines ? null! : [], 0, "CASH", PriceCop), CancellationToken.None);

        AssertRejected(result, ErrorCode.Validation, "La venta no puede estar vacía");
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("CARD")]
    [InlineData("")]
    [InlineData(null)]
    public async Task UnsupportedPayment_RejectsWithoutChanges(string? paymentMethod)
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var productId = await PrepareAsync(bed);
        var before = await SnapshotAsync();

        // RN-VEN-010: exactly CASH or TRANSFER; no additional payment methods.
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(productId)], 0, paymentMethod!, PriceCop), CancellationToken.None);

        AssertRejected(result, ErrorCode.Validation, "El medio de pago no es válido");
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task SignedOut_RejectsWithoutChanges()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var productId = await PrepareAsync(bed);
        var session = ProductTestBed.AdminSession();
        session.SignOut();
        var before = await SnapshotAsync();

        // RN-VEN-016 / RF-VEN-019: every sale belongs to an authenticated user.
        var result = await UseCase(bed, session).ExecuteAsync(
            new FinalizeSaleRequest([Line(productId)], 0, "CASH", PriceCop), CancellationToken.None);

        AssertRejected(result, ErrorCode.Authorization, "Debe iniciar sesión");
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("deleted", ErrorCode.NotFound, "ya no existe en el catálogo")]
    [InlineData("unit", ErrorCode.Conflict, "La unidad de «Leche entera» cambió")]
    [InlineData("plu", ErrorCode.Conflict, "«Leche entera» cambió en el catálogo")]
    [InlineData("name", ErrorCode.Conflict, "«Leche entera» cambió en el catálogo")]
    public async Task CatalogChangedAfterDraft_RejectsWithoutFurtherChanges(
        string change, ErrorCode code, string message)
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var productId = await PrepareAsync(bed);
        var draft = Line(productId);

        // RF-VEN-004/021: don't silently charge a different product/unit than the draft.
        // Target has zero stock and no history: changing its unit or deleting it is legal.
        await using (var connection = PostgresTestDb.NewConnection())
        {
            await connection.OpenAsync();
            var sql = change switch
            {
                "deleted" => "DELETE FROM Product WHERE ProductId = @ProductId;",
                "unit" => "UPDATE Product SET Unit = 'KG' WHERE ProductId = @ProductId;",
                "plu" => "UPDATE Product SET Plu = 'L2' WHERE ProductId = @ProductId;",
                "name" => "UPDATE Product SET Name = 'Leche nueva' WHERE ProductId = @ProductId;",
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
            Assert.Equal(1, await connection.ExecuteAsync(sql, new { ProductId = productId }));
        }

        // Preserve the legitimate catalog edit, plus the prior sale and cash totals.
        var before = await SnapshotAsync();
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([draft], 0, "CASH", PriceCop), CancellationToken.None);

        AssertRejected(result, code, message);
        Assert.Equal(before, await SnapshotAsync());
    }

    private static FinalizeSaleUseCase UseCase(ProductTestBed bed, ICurrentSession? session = null) =>
        new(bed.Sales, bed.Sessions, bed.Units, session ?? ProductTestBed.AdminSession(), bed.Clock, bed.Gate);

    private static DraftSaleLine Line(int productId, string plu = "L1", string name = "Leche entera") =>
        new(productId, plu, name, ProductUnit.Unit, PriceCop, 1_000);

    private static void AssertRejected(Result<FinalizeSaleResponse> result, ErrorCode code, string message)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error!.Code);
        Assert.Contains(message, result.Error.Message);
    }

    private static async Task<int> PrepareAsync(ProductTestBed bed)
    {
        await using (var connection = PostgresTestDb.NewConnection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
                + " VALUES (1, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
                new { Salt = new byte[16], Hash = new byte[32] });
        }

        var opened = await new OpenCashSessionUseCase(
                bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate)
            .ExecuteAsync(new OpenCashSessionRequest(50_000), CancellationToken.None);
        Assert.True(opened.IsSuccess, opened.Error?.Message);

        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var control = await create.ExecuteAsync(
            new CreateProductRequest("C1", "Control", ProductUnit.Unit, PriceCop, 5_000), CancellationToken.None);
        Assert.True(control.IsSuccess, control.Error?.Message);
        var priorSale = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(control.Value.ProductId, "C1", "Control")], 0, "CASH", PriceCop),
            CancellationToken.None);
        Assert.True(priorSale.IsSuccess, priorSale.Error?.Message);
        Assert.Equal(1, priorSale.Value.SaleNumber);

        var target = await create.ExecuteAsync(
            new CreateProductRequest("L1", "Leche entera", ProductUnit.Unit, PriceCop, 0), CancellationToken.None);
        Assert.True(target.IsSuccess, target.Error?.Message);
        return target.Value.ProductId;
    }

    private static async Task<string> SnapshotAsync()
    {
        // A fresh connection sees committed state. Compare all rows/columns, not just counts
        // or returned DTOs; this catches updates/deletions of pre-existing business data too.
        await using var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<string>("""
            SELECT json_build_object(
                'sales', (SELECT json_agg(t ORDER BY t.SaleId) FROM Sale t),
                'lines', (SELECT json_agg(t ORDER BY t.SaleId, t.LineNumber) FROM SaleLine t),
                'voids', (SELECT json_agg(t ORDER BY t.SaleId) FROM SaleVoid t),
                'products', (SELECT json_agg(t ORDER BY t.ProductId) FROM Product t),
                'cash', (SELECT json_agg(t ORDER BY t.CashSessionId) FROM CashSession t),
                'totals', (SELECT json_agg(t ORDER BY t.CashSessionId) FROM V_OpenCashTotals t),
                'sequences', (SELECT json_agg(t ORDER BY t.SequenceName) FROM SystemSequence t)
            )::text;
            """);
    }
}
