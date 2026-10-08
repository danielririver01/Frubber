using FrubberPOS.Application.Business;
using FrubberPOS.Application.Sales;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// Pure composition tests for the ticket (JHO-45, RN-IMP-004/005/006).
/// The composer never touches a database or a printer.
/// </summary>
public sealed class TicketComposerTests
{
    private static readonly DateTimeOffset SaleMoment = new(2026, 9, 28, 11, 42, 0, TimeSpan.FromHours(-5));

    private static TicketSaleData Sale(
        string paymentMethod = "CASH",
        long? receivedCop = 20_000,
        long? changeCop = 4_100,
        long discountCop = 0) => new(
        1,
        SaleMoment.ToUnixTimeMilliseconds(),
        (int)SaleMoment.Offset.TotalMinutes,
        "Ana Martínez",
        15_900,
        discountCop,
        15_900 - discountCop,
        paymentMethod,
        receivedCop,
        changeCop,
        [new TicketLineData(1, "Manzana Gala", "KG", 1_250, 6_000, 7_500),
         new TicketLineData(2, "Leche entera", "UNIT", 2_000, 4_200, 8_400)]);

    [Fact]
    public void CashSale_ShowsSaleNumberDateCashierTotalsAndChange()
    {
        var document = TicketComposer.Compose(Sale(), null);

        Assert.Equal("V-000001", document.SaleNumberText);
        Assert.Contains(document.Lines, line => line.Contains("Venta V-000001"));
        Assert.Contains(document.Lines, line => line.Contains("28/09/2026 11:42"));
        Assert.Contains(document.Lines, line => line.Contains("Cajero: Ana Martínez"));
        Assert.Contains(document.Lines, line => line.Contains("Subtotal") && line.Contains("$ 15.900"));
        Assert.Contains(document.Lines, line => line.Contains("TOTAL") && line.Contains("$ 15.900"));
        Assert.Contains(document.Lines, line => line.Contains("Pago: Efectivo"));
        Assert.Contains(document.Lines, line => line.Contains("Recibido") && line.Contains("$ 20.000"));
        Assert.Contains(document.Lines, line => line.Contains("Cambio") && line.Contains("$ 4.100"));
    }

    [Fact]
    public void TransferSale_OmitsReceivedAndChange()
    {
        var document = TicketComposer.Compose(Sale(paymentMethod: "TRANSFER", receivedCop: null, changeCop: null), null);

        Assert.Contains(document.Lines, line => line.Contains("Pago: Transferencia"));
        Assert.DoesNotContain(document.Lines, line => line.Contains("Recibido"));
        Assert.DoesNotContain(document.Lines, line => line.Contains("Cambio"));
    }

    [Fact]
    public void Lines_PrintHistoricalQuantityUnitPriceAndAmounts()
    {
        var document = TicketComposer.Compose(Sale(), null);

        // JHO-67 N4 (formato aprobado): "1,25 kg × $ 6.000/kg = $ 7.500" y
        // "2 unit × $ 4.200 = $ 8.400" — la suma iguala el subtotal del encabezado.
        Assert.Contains(document.Lines, line => line.Contains("Manzana Gala"));
        Assert.Contains(document.Lines, line =>
            line.Contains("1,25 kg × $ 6.000/kg = $ 7.500"));
        Assert.Contains(document.Lines, line =>
            line.Contains("2 unit × $ 4.200 = $ 8.400"));
    }

    [Fact]
    public void Discount_ShowsTheDiscountedAmount()
    {
        var document = TicketComposer.Compose(Sale(discountCop: 900), null);

        Assert.Contains(document.Lines, line => line.Contains("Descuento") && line.Contains("$ 900"));
        Assert.Contains(document.Lines, line => line.Contains("TOTAL") && line.Contains("$ 15.000"));
    }

    [Fact]
    public void ConfiguredBusiness_HeadsTheTicketAndClosesWithTheFooter()
    {
        var business = new BusinessSettingSnapshot(
            "Frutas Don José", "900.123.456-1", "310 555 1234", "Cra 10 # 20-30",
            "¡Gracias por su compra!", "POS-58");

        var document = TicketComposer.Compose(Sale(), business);

        Assert.Contains(document.Lines, line => line.Contains("Frutas Don José"));
        Assert.Contains(document.Lines, line => line.Contains("NIT 900.123.456-1"));
        Assert.Contains(document.Lines, line => line.Contains("Tel 310 555 1234"));
        Assert.Contains(document.Lines, line => line.Contains("Cra 10 # 20-30"));
        Assert.Contains(document.Lines, line => line.Contains("¡Gracias por su compra!"));
        Assert.Equal("POS-58", document.ConfiguredPrinterName);
    }

    [Fact]
    public void WithoutBusinessData_UsesTheProductNameAsHeader()
    {
        var document = TicketComposer.Compose(Sale(), null);

        Assert.Contains(document.Lines, line => line.Contains("Frubber POS"));
        Assert.Null(document.ConfiguredPrinterName);
    }
}
