using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Business;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Business configuration screen (JHO-51, §15.1/15.2): name, NIT, phone,
/// address, closing message and default printer. Empty text means "not
/// configured"; the approved limits appear when the field nears its
/// maximum. The printer list comes from Windows (installed printers or
/// "sin impresora") — never width nor device commands.
/// </summary>
public sealed class BusinessSettingsViewModel : INotifyPropertyChanged
{
    /// <summary>Printer selector entry for "none configured" (§15.2).</summary>
    public const string NoPrinterOption = "(Sin impresora configurada)";

    private const int HintThreshold = 10;

    private readonly GetBusinessSettingsUseCase getSettings;
    private readonly UpdateBusinessSettingsUseCase updateSettings;
    private readonly ObservableCollection<string> printerOptions = [];

    private string businessNameText = string.Empty;
    private string taxIdentifierText = string.Empty;
    private string phoneText = string.Empty;
    private string addressText = string.Empty;
    private string footerText = string.Empty;
    private string? selectedPrinterOption;
    private string statusMessage = string.Empty;
    private string errorMessage = string.Empty;
    private bool loaded;

    public BusinessSettingsViewModel(GetBusinessSettingsUseCase getSettings, UpdateBusinessSettingsUseCase updateSettings)
    {
        ArgumentNullException.ThrowIfNull(getSettings);
        ArgumentNullException.ThrowIfNull(updateSettings);
        this.getSettings = getSettings;
        this.updateSettings = updateSettings;
        SaveCommand = new AsyncCommand(_ => SaveAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> PrinterOptions => printerOptions;

    public string BusinessNameText
    {
        get => businessNameText;
        set { if (SetProperty(ref businessNameText, value)) OnPropertyChanged(nameof(BusinessNameHintText)); }
    }

    public string TaxIdentifierText
    {
        get => taxIdentifierText;
        set { if (SetProperty(ref taxIdentifierText, value)) OnPropertyChanged(nameof(TaxIdentifierHintText)); }
    }

    public string PhoneText
    {
        get => phoneText;
        set { if (SetProperty(ref phoneText, value)) OnPropertyChanged(nameof(PhoneHintText)); }
    }

    public string AddressText
    {
        get => addressText;
        set { if (SetProperty(ref addressText, value)) OnPropertyChanged(nameof(AddressHintText)); }
    }

    public string FooterText
    {
        get => footerText;
        set { if (SetProperty(ref footerText, value)) OnPropertyChanged(nameof(FooterHintText)); }
    }

    public string? SelectedPrinterOption
    {
        get => selectedPrinterOption;
        set => SetProperty(ref selectedPrinterOption, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    // §15.1: the approved limit appears only when nearing the maximum.
    public string BusinessNameHintText => Hint(BusinessNameText, UpdateBusinessSettingsUseCase.BusinessNameMaxLength);

    public string TaxIdentifierHintText => Hint(TaxIdentifierText, UpdateBusinessSettingsUseCase.TaxIdentifierMaxLength);

    public string PhoneHintText => Hint(PhoneText, UpdateBusinessSettingsUseCase.PhoneMaxLength);

    public string AddressHintText => Hint(AddressText, UpdateBusinessSettingsUseCase.AddressMaxLength);

    public string FooterHintText => Hint(FooterText, UpdateBusinessSettingsUseCase.TicketFooterMaxLength);

    public AsyncCommand SaveCommand { get; }

    /// <summary>Loads the row and the installed Windows printers (once per view).</summary>
    public async Task InitializeAsync(IReadOnlyList<string> installedPrinters)
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        ArgumentNullException.ThrowIfNull(installedPrinters);

        printerOptions.Clear();
        printerOptions.Add(NoPrinterOption);
        foreach (var printer in installedPrinters)
        {
            printerOptions.Add(printer);
        }

        var result = await getSettings.ExecuteAsync(new GetBusinessSettingsRequest(), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            SelectedPrinterOption = NoPrinterOption;
            return;
        }

        var settings = result.Value;
        BusinessNameText = settings.BusinessName ?? string.Empty;
        TaxIdentifierText = settings.TaxIdentifier ?? string.Empty;
        PhoneText = settings.Phone ?? string.Empty;
        AddressText = settings.Address ?? string.Empty;
        FooterText = settings.TicketFooterMessage ?? string.Empty;
        SelectedPrinterOption = settings.PrinterName is not null && printerOptions.Contains(settings.PrinterName)
            ? settings.PrinterName
            : NoPrinterOption;
    }

    public async Task SaveAsync()
    {
        var result = await updateSettings.ExecuteAsync(
            new UpdateBusinessSettingsRequest(
                BusinessNameText,
                TaxIdentifierText,
                PhoneText,
                AddressText,
                FooterText,
                SelectedPrinterOption == NoPrinterOption ? null : SelectedPrinterOption),
            CancellationToken.None);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            StatusMessage = string.Empty;
            return;
        }

        ErrorMessage = string.Empty;
        StatusMessage = "Configuración guardada.";
    }

    private static string Hint(string text, int maxLength)
    {
        var remaining = maxLength - text.Length;
        return remaining <= HintThreshold ? $"{text.Length}/{maxLength}" : string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
