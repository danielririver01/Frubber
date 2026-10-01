using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Products;

namespace FrubberPOS.Application.Navigation;

/// <summary>
/// Screen-flow contract: first-start, login, shell and license (JHO-29,
/// JHO-25). Declared here (not in Desktop) so ViewModels stay testable
/// without referencing the WPF assembly, per the P-DD §18.4 ViewModel test
/// strategy. Carries no window, dialog or dispatcher types — those live in
/// the Desktop implementation owned by <c>App</c>. ViewModels navigate
/// through this, never through window types.
/// </summary>
public interface INavigationService
{
    void ShowFirstStart();

    void ShowLogin();

    void ShowShell(SignedInUser user);

    void ShowLicense();

    /// <summary>Product editor dialog (JHO-30); <c>null</c> creates. Returns true when saved.</summary>
    bool ShowProductEditor(ProductDto? existing);

    /// <summary>Direct stock adjustment dialog (JHO-33). Returns true when saved.</summary>
    bool ShowAdjustStock(ProductDto product);

    /// <summary>Delete confirmation for a product (spec §12.5). <c>true</c> when confirmed.</summary>
    bool ConfirmProductDeletion(ProductDto product);

    /// <summary>Cash close confirmation, irreversible (spec §11.3). <c>true</c> when confirmed.</summary>
    bool ConfirmCashClosure();

    /// <summary>Discard-draft confirmation (P-UX-013). <c>true</c> when confirmed.</summary>
    bool ConfirmDiscardDraft();

    /// <summary>Quantity dialog for the product (P-UX-009). <c>null</c> = canceled.</summary>
    long? AskQuantity(ProductDto product);

    /// <summary>Checkout modal (P-UX-008, JHO-41). Confirmed = vender; otherwise canceled.</summary>
    CheckoutResultDto ShowCheckout(long subtotalCop, long pendingDiscountCop);

    /// <summary>
    /// Post-sale confirmation (P-UX §9.5, JHO-45): shows the saved sale and
    /// offers the optional print. <c>true</c> = operator wants the ticket.
    /// </summary>
    bool ShowSaleSaved(SaleSavedSummary sale);

    /// <summary>
    /// Printer picker (JHO-45): lists the installed Windows printers,
    /// preselecting the configured one. <c>null</c> = no printer / cancel
    /// (printing stays optional per RN-IMP-005).
    /// </summary>
    string? AskPrinter(string? configuredPrinterName);
}

/// <summary>Checkout modal answer (JHO-41). Whole COP only.</summary>
public sealed record CheckoutResultDto(bool Confirmed, string PaymentMethod, long ReceivedCop, long ChangeCop, long DiscountCop);

/// <summary>
/// Summary shown right after a sale is saved (JHO-45, P-UX §9.5).
/// <c>ReceivedCop</c>/<c>ChangeCop</c> are present only for cash.
/// </summary>
public sealed record SaleSavedSummary(long SaleNumber, long TotalCop, string PaymentMethod, long? ReceivedCop, long? ChangeCop);
