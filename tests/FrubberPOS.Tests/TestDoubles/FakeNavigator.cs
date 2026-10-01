using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Products;

namespace FrubberPOS.Tests.TestDoubles;

/// <summary>
/// Records navigation calls for ViewModel tests. Production navigation is
/// owned by <c>App</c>.
/// </summary>
public sealed class FakeNavigator : INavigationService
{
    public int FirstStartCount { get; private set; }

    public int LoginCount { get; private set; }

    public int ShellCount { get; private set; }

    public int LicenseCount { get; private set; }

    public int ProductEditorCount { get; private set; }

    public int AdjustStockCount { get; private set; }

    public int ConfirmCount { get; private set; }

    public int ConfirmDeletionCount { get; private set; }

    public int ConfirmCashClosureCount { get; private set; }

    public int ConfirmDiscardDraftCount { get; private set; }

    public int AskQuantityCount { get; private set; }

    public int ShowCheckoutCount { get; private set; }

    public int ShowSaleSavedCount { get; private set; }

    public int AskPrinterCount { get; private set; }

    public SignedInUser? LastShellUser { get; private set; }

    public ProductDto? LastEditedProduct { get; private set; }

    public SaleSavedSummary? LastSaleSaved { get; private set; }

    public string? LastConfiguredPrinterName { get; private set; }

    public bool NextEditorResult { get; set; }

    public bool NextAdjustResult { get; set; }

    public bool NextConfirmResult { get; set; } = true;

    public long? NextQuantityMilli { get; set; }

    public CheckoutResultDto? NextCheckout { get; set; }

    /// <summary>Post-sale choice (JHO-45): true = print the ticket.</summary>
    public bool NextSaleSavedPrintRequested { get; set; }

    /// <summary>Printer picked in the picker; null = no printing.</summary>
    public string? NextPrinterName { get; set; }

    public void ShowFirstStart() => FirstStartCount++;

    public void ShowLogin() => LoginCount++;

    public void ShowLicense() => LicenseCount++;

    public void ShowShell(SignedInUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        ShellCount++;
        LastShellUser = user;
    }

    public bool ShowProductEditor(ProductDto? existing)
    {
        ProductEditorCount++;
        LastEditedProduct = existing;
        return NextEditorResult;
    }

    public bool ShowAdjustStock(ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(product);
        AdjustStockCount++;
        return NextAdjustResult;
    }

    public bool ConfirmProductDeletion(ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(product);
        ConfirmDeletionCount++;
        return NextConfirmResult;
    }

    public bool ConfirmCashClosure()
    {
        ConfirmCashClosureCount++;
        return NextConfirmResult;
    }

    public bool ConfirmDiscardDraft()
    {
        ConfirmDiscardDraftCount++;
        return NextConfirmResult;
    }

    public long? AskQuantity(ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(product);
        AskQuantityCount++;
        return NextQuantityMilli;
    }

    public CheckoutResultDto ShowCheckout(long subtotalCop, long pendingDiscountCop)
    {
        ShowCheckoutCount++;
        return NextCheckout ?? new CheckoutResultDto(false, "CASH", 0, 0, 0);
    }

    public bool ShowSaleSaved(SaleSavedSummary sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ShowSaleSavedCount++;
        LastSaleSaved = sale;
        return NextSaleSavedPrintRequested;
    }

    public string? AskPrinter(string? configuredPrinterName)
    {
        AskPrinterCount++;
        LastConfiguredPrinterName = configuredPrinterName;
        return NextPrinterName;
    }
}
