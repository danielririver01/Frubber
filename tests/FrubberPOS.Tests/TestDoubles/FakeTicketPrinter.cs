using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Tests.TestDoubles;

/// <summary>
/// No-op printer seam for ViewModel tests (JHO-45): records calls and can
/// be armed to fail. Never touches a real Windows printer.
/// </summary>
public sealed class FakeTicketPrinter : ITicketPrinter
{
    public int PrintCount { get; private set; }

    public TicketDocument? LastTicket { get; private set; }

    public string? LastPrinterName { get; private set; }

    /// <summary>Error message to return on the next print; null = success.</summary>
    public string? NextError { get; set; }

    public string? Print(TicketDocument ticket, string printerName)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        PrintCount++;
        LastTicket = ticket;
        LastPrinterName = printerName;
        return NextError;
    }
}
