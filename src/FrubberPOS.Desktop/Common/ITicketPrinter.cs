using System.Drawing.Printing;
using FrubberPOS.Application.Sales;

namespace FrubberPOS.Desktop.Common;

/// <summary>
/// Ticket printing seam for the Desktop layer (JHO-45). Printing happens
/// only after the sale is committed and never re-runs the cobro
/// (RN-IMP-001/005). Failures come back as a safe Spanish message instead
/// of an exception so the UI can say "la venta quedó guardada" (P-UX §9.6).
/// </summary>
public interface ITicketPrinter
{
    /// <summary><c>null</c> on success; otherwise a safe operator message.</summary>
    string? Print(TicketDocument ticket, string printerName);
}

/// <summary>
/// Windows-only ticket printer over <see cref="PrintDocument"/> (ADR-018:
/// API de impresión de Windows, never raw ESC/POS). Blocking by design on
/// the UI confirmation path: it only runs when the operator asked for it.
/// </summary>
public sealed class WindowsTicketPrinter : ITicketPrinter
{
    public static IReadOnlyList<string> InstalledPrinters() =>
        PrinterSettings.InstalledPrinters.Cast<string>().ToList();

    public string? Print(TicketDocument ticket, string printerName)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (string.IsNullOrWhiteSpace(printerName))
        {
            return "No se seleccionó una impresora.";
        }

        try
        {
            using var document = new PrintDocument { DocumentName = ticket.Title };
            document.PrinterSettings.PrinterName = printerName;
            if (!document.PrinterSettings.IsValid)
            {
                return "La impresora seleccionada no está disponible.";
            }

            using var font = new System.Drawing.Font("Consolas", 9F);
            var lines = ticket.Lines;
            var next = 0;
            document.PrintPage += (_, args) =>
            {
                if (args.Graphics is null)
                {
                    return;
                }

                var y = (float)args.MarginBounds.Top;
                while (next < lines.Count)
                {
                    var height = font.GetHeight(args.Graphics);
                    if (y + height > args.MarginBounds.Bottom)
                    {
                        args.HasMorePages = true;
                        return;
                    }

                    args.Graphics.DrawString(lines[next], font, System.Drawing.Brushes.Black, (float)args.MarginBounds.Left, y);
                    y += height;
                    next++;
                }

                args.HasMorePages = false;
            };
            document.Print();
            return null;
        }
        catch (InvalidPrinterException)
        {
            return "La impresora seleccionada no está disponible.";
        }
        catch (Exception)
        {
            // Never leak internals to the operator; the sale is already
            // saved and this failure only affects the paper.
            return "El ticket no pudo imprimirse.";
        }
    }
}
