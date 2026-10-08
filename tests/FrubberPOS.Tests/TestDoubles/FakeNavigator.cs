using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Users;

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

    public int LockCount { get; private set; }

    public int ProductEditorCount { get; private set; }

    public int AdjustStockCount { get; private set; }

    public int ConfirmCount { get; private set; }

    public int ConfirmDeletionCount { get; private set; }

    public int ConfirmCashClosureCount { get; private set; }

    public int ConfirmDiscardDraftCount { get; private set; }

    public int AskQuantityCount { get; private set; }

    public int ShowCheckoutCount { get; private set; }

    public int LastCheckoutItemCount { get; private set; }

    public bool LastCheckoutDiscountMode { get; private set; }

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

    public int ShowSaleDetailCount { get; private set; }

    public long? LastSaleDetailNumber { get; private set; }

    public int ConfirmSaleVoidCount { get; private set; }

    /// <summary>Detail dialog outcome: true = la venta quedó anulada ahí.</summary>
    public bool NextSaleDetailVoided { get; set; }

    /// <summary>Void confirmation answer (P-UX §13.4).</summary>
    public bool NextSaleVoidConfirmed { get; set; } = true;

    public int AskBackupDestinationCount { get; private set; }

    /// <summary>Backup destination chosen in the dialog; null = canceled.</summary>
    public string? NextBackupDestination { get; set; }

    public int ShowUserEditorCount { get; private set; }

    public int ShowPinResetCount { get; private set; }

    public int ConfirmUserDeactivationCount { get; private set; }

    public int ConfirmUserDeletionCount { get; private set; }

    public UserDetailDto? LastEditedUser { get; private set; }

    public bool NextUserEditorSaved { get; set; }

    public bool NextPinResetSaved { get; set; }

    public int AskOpeningBalanceCorrectionCount { get; private set; }

    /// <summary>Saldo corregido devuelto por el diálogo; null = cancelado.</summary>
    public long? NextOpeningBalanceCorrection { get; set; }

    /// <summary>
    /// One-shot hook fired inside <see cref="ShowCheckout"/>: simulates a
    /// re-entrant Enter/click while the dialog is open (JHO-39).
    /// </summary>
    public Action? OnShowCheckout { get; set; }

    /// <summary>
    /// One-shot hook fired inside <see cref="ShowSaleSaved"/>: lets a test
    /// observe the TPV state while the sale is still finalizing (JHO-67 B2).
    /// </summary>
    public Action? OnShowSaleSaved { get; set; }

    public void ShowFirstStart() => FirstStartCount++;

    public void ShowLogin() => LoginCount++;

    public void ShowLicense() => LicenseCount++;

    public void ShowLock() => LockCount++;

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

    public CheckoutResultDto ShowCheckout(long subtotalCop, long pendingDiscountCop, int itemCount, bool discountMode)
    {
        ShowCheckoutCount++;
        LastCheckoutItemCount = itemCount;
        LastCheckoutDiscountMode = discountMode;
        if (OnShowCheckout is { } hook)
        {
            OnShowCheckout = null; // one-shot: re-entry must not recurse
            hook();
        }

        return NextCheckout ?? new CheckoutResultDto(false, "CASH", 0, 0, 0);
    }

    public bool ShowSaleSaved(SaleSavedSummary sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ShowSaleSavedCount++;
        LastSaleSaved = sale;
        if (OnShowSaleSaved is { } hook)
        {
            OnShowSaleSaved = null; // one-shot
            hook();
        }

        return NextSaleSavedPrintRequested;
    }

    public string? AskPrinter(string? configuredPrinterName)
    {
        AskPrinterCount++;
        LastConfiguredPrinterName = configuredPrinterName;
        return NextPrinterName;
    }

    public bool ShowSaleDetail(long saleNumber)
    {
        ShowSaleDetailCount++;
        LastSaleDetailNumber = saleNumber;
        return NextSaleDetailVoided;
    }

    public bool ConfirmSaleVoid(long saleNumber)
    {
        ConfirmSaleVoidCount++;
        return NextSaleVoidConfirmed;
    }

    public string? AskBackupDestination()
    {
        AskBackupDestinationCount++;
        return NextBackupDestination;
    }

    public bool ShowUserEditor(UserDetailDto? existing)
    {
        ShowUserEditorCount++;
        LastEditedUser = existing;
        return NextUserEditorSaved;
    }

    public bool ShowPinReset(UserDetailDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        ShowPinResetCount++;
        return NextPinResetSaved;
    }

    public bool ConfirmUserDeactivation(UserDetailDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        ConfirmUserDeactivationCount++;
        return NextConfirmResult;
    }

    public bool ConfirmUserDeletion(UserDetailDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        ConfirmUserDeletionCount++;
        return NextConfirmResult;
    }

    public long? AskOpeningBalanceCorrection(long currentBalanceCop)
    {
        AskOpeningBalanceCorrectionCount++;
        return NextOpeningBalanceCorrection;
    }
}
