using System.Globalization;
using FrubberPOS.Application.Business;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Application.Sales;

/// <summary>
/// Pure ticket composition (JHO-45, RN-IMP-004/005/006): builds the
/// fixed-width plain-text ticket from the historical sale snapshots and
/// the configured business data. Whole COP and thousandths only — never
/// floating point (ADR-009). Nothing here touches Windows printing or
/// the draft; Desktop renders these lines as-is and the sale is never
/// re-executed to print.
/// </summary>
public static class TicketComposer
{
    /// <summary>Column width aimed at 58/80 mm receipt printers.</summary>
    public const int Width = 32;

    private static readonly string Separator = new('-', Width);

    public static TicketDocument Compose(TicketSaleData sale, BusinessSettingSnapshot? business)
    {
        ArgumentNullException.ThrowIfNull(sale);

        var saleNumberText = $"V-{sale.SaleNumber:D6}";
        var lines = new List<string>();

        // Business header (RN-IMP-006): only configured values surface;
        // with no configuration the product name heads the ticket.
        var businessName = business?.BusinessName;
        lines.Add(Center(string.IsNullOrWhiteSpace(businessName) ? "Frubber POS" : businessName!.Trim()));
        if (!string.IsNullOrWhiteSpace(business?.TaxIdentifier))
        {
            lines.Add(Center($"NIT {business!.TaxIdentifier}"));
        }

        if (!string.IsNullOrWhiteSpace(business?.Phone))
        {
            lines.Add(Center($"Tel {business!.Phone}"));
        }

        if (!string.IsNullOrWhiteSpace(business?.Address))
        {
            lines.Add(Center(business!.Address!));
        }

        lines.Add(Separator);
        lines.Add($"Venta {saleNumberText}");
        lines.Add(FormatSoldAt(sale.SoldAtUtcMs, sale.SoldAtOffsetMinutes));
        lines.Add($"Cajero: {sale.CashierName}");
        lines.Add(Separator);

        // Historical lines (RN-IMP-004): JHO-67 N4 formato aprobado por el
        // dueño — "1,35 kg × $ 12.000/kg = $ 16.200" (comas es-CO, minúsculas,
        // sufijo /kg solo en pesados). Si no cabe en el ancho, se parte en
        // detalle + total alineado a la derecha (formato previo).
        foreach (var line in sale.Lines)
        {
            lines.Add(TrimTo(line.Name, Width));
            lines.Add(FormatLineDetail(line));
        }

        lines.Add(Separator);
        lines.Add(WithRight("Subtotal", FormatCop(sale.SubtotalCop)));
        lines.Add(WithRight("Descuento", sale.DiscountCop == 0 ? FormatCop(0) : "-" + FormatCop(sale.DiscountCop)));
        lines.Add(WithRight("TOTAL", FormatCop(sale.TotalCop)));

        // Medium of payment; cash adds received/change (RN-IMP-005).
        lines.Add(sale.PaymentMethod == "CASH" ? "Pago: Efectivo" : "Pago: Transferencia");
        if (sale.PaymentMethod == "CASH" && sale.ReceivedCop is not null)
        {
            lines.Add(WithRight("Recibido", FormatCop(sale.ReceivedCop.Value)));
            lines.Add(WithRight("Cambio", FormatCop(sale.ChangeCop ?? 0)));
        }

        if (!string.IsNullOrWhiteSpace(business?.TicketFooterMessage))
        {
            lines.Add(Separator);
            lines.Add(Center(TrimTo(business!.TicketFooterMessage!, Width)));
        }

        return new TicketDocument(
            saleNumberText,
            $"Ticket {saleNumberText}",
            lines,
            business?.PrinterName);
    }

    private static string FormatSoldAt(long soldAtUtcMs, int offsetMinutes)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(soldAtUtcMs)
            .ToOffset(TimeSpan.FromMinutes(offsetMinutes));
        return local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    // JHO-67 N4: "1,35 kg × $ 12.000/kg = $ 16.200" (comas es-CO, unidades
    // en minúsculas, sufijo /unit solo en pesados como el proto .per). Si no
    // cabe en el ancho, se parte en detalle + total alineado a la derecha.
    private static string FormatLineDetail(TicketLineData line)
    {
        var quantity = FormatQuantity(line.QuantityMilli, line.UnitCode);
        var price = FormatCop(line.UnitPriceCop);
        var total = FormatCop(line.LineTotalCop);
        var unit = line.UnitCode.ToLowerInvariant();
        var detail = line.UnitCode == "UNIT"
            ? $"{quantity} unit × {price} = {total}"
            : $"{quantity} {unit} × {price}/{unit} = {total}";
        return detail.Length <= Width
            ? detail
            : WithRight($"  {quantity} {line.UnitCode} x {price}", total);
    }

    // JHO-67 N4: fracción con coma decimal (es-CO), como en pantalla.
    private static string FormatQuantity(long milli, string unitCode)
    {
        if (unitCode == "UNIT")
        {
            return (milli / ProductQuantities.Scale).ToString(CultureInfo.InvariantCulture);
        }

        var integerPart = milli / ProductQuantities.Scale;
        var fraction = (milli % ProductQuantities.Scale).ToString("000", CultureInfo.InvariantCulture).TrimEnd('0');
        return fraction.Length == 0
            ? integerPart.ToString(CultureInfo.InvariantCulture)
            : integerPart.ToString(CultureInfo.InvariantCulture) + "," + fraction;
    }

    // Same whole-COP format as Desktop MoneyText ("$ 6.000", es-CO
    // groups). Kept local so Application never depends on Desktop.
    private static string FormatCop(long cop) =>
        "$ " + cop.ToString("N0", CultureInfo.GetCultureInfo("es-CO"));

    private static string Center(string text) =>
        text.Length >= Width ? text : text.PadLeft((Width + text.Length) / 2);

    private static string TrimTo(string text, int width) =>
        text.Length <= width ? text : text[..width];

    private static string WithRight(string left, string right) =>
        left.Length + right.Length <= Width
            ? left + right.PadLeft(Width - left.Length)
            : left + " " + right;
}
