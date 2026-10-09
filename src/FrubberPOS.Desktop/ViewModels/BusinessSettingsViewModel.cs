using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrubberPOS.Application.Business;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Business configuration screen (JHO-51, §15.1/15.2; JHO-79 rediseño):
/// name, NIT, phone, address, closing message and default printer. Empty
/// text means "not configured". The printer list comes from Windows.
/// JHO-79 adds section navigation, dirty tracking, live ticket preview and
/// a test print — still the same single BusinessSetting row.
/// </summary>
public sealed class BusinessSettingsViewModel : INotifyPropertyChanged
{
    /// <summary>Printer selector entry for "none configured" (§15.2).</summary>
    public const string NoPrinterOption = "(Sin impresora configurada)";

    /// <summary>Section keys for the left nav (JHO-79).</summary>
    public const string BusinessSectionKey = "business";

    public const string PrinterSectionKey = "printer";

    private const int HintThreshold = 10;
    private const int TicketWidth = 32;

    private readonly GetBusinessSettingsUseCase getSettings;
    private readonly UpdateBusinessSettingsUseCase updateSettings;
    private readonly ITicketPrinter? ticketPrinter;
    private readonly ObservableCollection<string> printerOptions = [];

    private string businessNameText = string.Empty;
    private string taxIdentifierText = string.Empty;
    private string phoneText = string.Empty;
    private string addressText = string.Empty;
    private string footerText = string.Empty;
    private string? selectedPrinterOption;
    private string statusMessage = string.Empty;
    private string errorMessage = string.Empty;
    private string selectedSectionKey = BusinessSectionKey;
    private bool isDirty;
    private bool loaded;
    private Snapshot saved = Snapshot.Empty;

    public BusinessSettingsViewModel(
        GetBusinessSettingsUseCase getSettings,
        UpdateBusinessSettingsUseCase updateSettings,
        ITicketPrinter? ticketPrinter = null)
    {
        ArgumentNullException.ThrowIfNull(getSettings);
        ArgumentNullException.ThrowIfNull(updateSettings);
        this.getSettings = getSettings;
        this.updateSettings = updateSettings;
        this.ticketPrinter = ticketPrinter;
        SaveCommand = new AsyncCommand(_ => SaveAsync());
        DiscardCommand = new AsyncCommand(_ =>
        {
            Discard();
            return Task.CompletedTask;
        });
        PrintTestCommand = new AsyncCommand(_ => PrintTestAsync(), () => CanPrintTest);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> PrinterOptions => printerOptions;

    public string BusinessNameText
    {
        get => businessNameText;
        set
        {
            if (SetProperty(ref businessNameText, value))
            {
                OnPropertyChanged(nameof(BusinessNameHintText));
                RefreshDerived();
            }
        }
    }

    public string TaxIdentifierText
    {
        get => taxIdentifierText;
        set
        {
            if (SetProperty(ref taxIdentifierText, value))
            {
                OnPropertyChanged(nameof(TaxIdentifierHintText));
                RefreshDerived();
            }
        }
    }

    public string PhoneText
    {
        get => phoneText;
        set
        {
            if (SetProperty(ref phoneText, value))
            {
                OnPropertyChanged(nameof(PhoneHintText));
                RefreshDerived();
            }
        }
    }

    public string AddressText
    {
        get => addressText;
        set
        {
            if (SetProperty(ref addressText, value))
            {
                OnPropertyChanged(nameof(AddressHintText));
                RefreshDerived();
            }
        }
    }

    public string FooterText
    {
        get => footerText;
        set
        {
            if (SetProperty(ref footerText, value))
            {
                OnPropertyChanged(nameof(FooterHintText));
                RefreshDerived();
            }
        }
    }

    public string? SelectedPrinterOption
    {
        get => selectedPrinterOption;
        set
        {
            if (SetProperty(ref selectedPrinterOption, value))
            {
                RefreshDerived();
            }
        }
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

    /// <summary>Active section key for the left nav (JHO-79).</summary>
    public string SelectedSectionKey
    {
        get => selectedSectionKey;
        set
        {
            if (!SetProperty(ref selectedSectionKey, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SectionTitle));
            OnPropertyChanged(nameof(IsBusinessSection));
            OnPropertyChanged(nameof(IsPrinterSection));
        }
    }

    public string SectionTitle =>
        SelectedSectionKey == PrinterSectionKey ? "Impresora predeterminada" : "Datos del negocio";

    public bool IsBusinessSection => SelectedSectionKey != PrinterSectionKey;

    public bool IsPrinterSection => SelectedSectionKey == PrinterSectionKey;

    /// <summary>True when the form differs from the last load/save snapshot.</summary>
    public bool IsDirty
    {
        get => isDirty;
        private set => SetProperty(ref isDirty, value);
    }

    /// <summary>Display name for the printer card / preview footer.</summary>
    public string PrinterDisplayText =>
        SelectedPrinterOption is null || SelectedPrinterOption == NoPrinterOption
            ? "Sin impresora"
            : SelectedPrinterOption;

    public bool CanPrintTest =>
        loaded
        && !string.IsNullOrWhiteSpace(SelectedPrinterOption)
        && SelectedPrinterOption != NoPrinterOption;

    /// <summary>
    /// Live 32-column preview of the ticket header/footer (JHO-79). Sample
    /// sale lines are static so the operator sees the real shape; only the
    /// business fields update as they type.
    /// </summary>
    public string TicketPreviewText => BuildTicketPreview();

    // §15.1: the approved limit appears only when nearing the maximum.
    public string BusinessNameHintText => Hint(BusinessNameText, UpdateBusinessSettingsUseCase.BusinessNameMaxLength);

    public string TaxIdentifierHintText => Hint(TaxIdentifierText, UpdateBusinessSettingsUseCase.TaxIdentifierMaxLength);

    public string PhoneHintText => Hint(PhoneText, UpdateBusinessSettingsUseCase.PhoneMaxLength);

    public string AddressHintText => Hint(AddressText, UpdateBusinessSettingsUseCase.AddressMaxLength);

    public string FooterHintText => Hint(FooterText, UpdateBusinessSettingsUseCase.TicketFooterMaxLength);

    public AsyncCommand SaveCommand { get; }

    public AsyncCommand DiscardCommand { get; }

    public AsyncCommand PrintTestCommand { get; }

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
            CaptureSnapshot();
            RefreshDerived();
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
        CaptureSnapshot();
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        RefreshDerived();
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
        CaptureSnapshot();
        RefreshDerived();
    }

    /// <summary>Restores the last load/save snapshot (JHO-79 barra amarilla).</summary>
    public void Discard()
    {
        BusinessNameText = saved.BusinessName;
        TaxIdentifierText = saved.TaxIdentifier;
        PhoneText = saved.Phone;
        AddressText = saved.Address;
        FooterText = saved.Footer;
        SelectedPrinterOption = saved.PrinterOption is not null && printerOptions.Contains(saved.PrinterOption)
            ? saved.PrinterOption
            : NoPrinterOption;
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        RefreshDerived();
    }

    /// <summary>Sends a sample ticket with the live business header to the selected printer.</summary>
    public async Task PrintTestAsync()
    {
        if (ticketPrinter is null)
        {
            return;
        }

        if (!CanPrintTest)
        {
            ErrorMessage = "Selecciona una impresora instalada antes de imprimir la prueba.";
            StatusMessage = string.Empty;
            return;
        }

        var ticket = BuildTestTicket();
        var error = await Task.Run(() => ticketPrinter.Print(ticket, SelectedPrinterOption!));
        if (error is null)
        {
            ErrorMessage = string.Empty;
            StatusMessage = "Ticket de prueba enviado.";
            return;
        }

        ErrorMessage = error;
        StatusMessage = string.Empty;
    }

    private TicketDocument BuildTestTicket()
    {
        var lines = new List<string>
        {
            Center(string.IsNullOrWhiteSpace(BusinessNameText) ? "Frubber POS" : BusinessNameText.Trim()),
        };

        if (!string.IsNullOrWhiteSpace(TaxIdentifierText))
        {
            lines.Add(Center($"NIT {TaxIdentifierText.Trim()}"));
        }

        if (!string.IsNullOrWhiteSpace(PhoneText))
        {
            lines.Add(Center($"Tel {PhoneText.Trim()}"));
        }

        if (!string.IsNullOrWhiteSpace(AddressText))
        {
            lines.Add(Center(AddressText.Trim()));
        }

        lines.Add(new string('-', TicketWidth));
        lines.Add("Ticket de prueba");
        lines.Add(Center(DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)));
        lines.Add(new string('-', TicketWidth));
        lines.Add("Aguacate 1,5 kg");
        lines.Add("  1,5 kg x $ 12.000/kg = $ 18.000");
        lines.Add(new string('-', TicketWidth));
        lines.Add(WithRight("TOTAL", "$ 18.000"));

        if (!string.IsNullOrWhiteSpace(FooterText))
        {
            lines.Add(new string('-', TicketWidth));
            lines.Add(Center(TrimTo(FooterText.Trim(), TicketWidth)));
        }

        return new TicketDocument(
            "TEST-000",
            "Ticket de prueba",
            lines,
            SelectedPrinterOption == NoPrinterOption ? null : SelectedPrinterOption);
    }

    private string BuildTicketPreview()
    {
        var lines = new List<string>
        {
            Center(string.IsNullOrWhiteSpace(BusinessNameText) ? "Frubber POS" : BusinessNameText.Trim()),
        };

        if (!string.IsNullOrWhiteSpace(TaxIdentifierText))
        {
            lines.Add(Center($"NIT {TaxIdentifierText.Trim()}"));
        }

        if (!string.IsNullOrWhiteSpace(PhoneText))
        {
            lines.Add(Center($"Tel {PhoneText.Trim()}"));
        }

        if (!string.IsNullOrWhiteSpace(AddressText))
        {
            lines.Add(Center(AddressText.Trim()));
        }

        lines.Add(new string('-', TicketWidth));
        lines.Add("Ticket V-000001");
        lines.Add(Center(DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)));
        lines.Add(new string('-', TicketWidth));
        lines.Add("Aguacate 1,5 kg");
        lines.Add("  1,5 kg x $ 12.000/kg = $ 18.000");
        lines.Add("Café molido 2 und");
        lines.Add("  2 und x $ 9.800 = $ 19.600");
        lines.Add(new string('-', TicketWidth));
        lines.Add(WithRight("Subtotal", "$ 37.600"));
        lines.Add(WithRight("TOTAL", "$ 37.600"));
        lines.Add("Pago: Efectivo");

        if (!string.IsNullOrWhiteSpace(FooterText))
        {
            lines.Add(new string('-', TicketWidth));
            foreach (var raw in FooterText.Split('\n'))
            {
                var part = raw.Trim();
                if (part.Length > 0)
                {
                    lines.Add(Center(TrimTo(part, TicketWidth)));
                }
            }
        }

        lines.Add(string.Empty);
        lines.Add(Center($"Impresora: {TrimTo(PrinterDisplayText, 20)}"));
        return string.Join(Environment.NewLine, lines);
    }

    private void RefreshDerived()
    {
        IsDirty = CurrentSnapshot() != saved;
        OnPropertyChanged(nameof(TicketPreviewText));
        OnPropertyChanged(nameof(PrinterDisplayText));
        OnPropertyChanged(nameof(CanPrintTest));
        PrintTestCommand.RaiseCanExecuteChanged();
    }

    private void CaptureSnapshot() => saved = CurrentSnapshot();

    private Snapshot CurrentSnapshot() => new(
        BusinessNameText,
        TaxIdentifierText,
        PhoneText,
        AddressText,
        FooterText,
        SelectedPrinterOption);

    private static string Hint(string text, int maxLength)
    {
        var remaining = maxLength - text.Length;
        return remaining <= HintThreshold ? $"{text.Length}/{maxLength}" : string.Empty;
    }

    private static string Center(string text) =>
        text.Length >= TicketWidth ? text : text.PadLeft((TicketWidth + text.Length) / 2);

    private static string WithRight(string left, string right) =>
        left.Length + right.Length <= TicketWidth
            ? left + right.PadLeft(TicketWidth - left.Length)
            : left + " " + right;

    private static string TrimTo(string text, int width) =>
        text.Length <= width ? text : text[..width];

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

    private sealed record Snapshot(
        string BusinessName,
        string TaxIdentifier,
        string Phone,
        string Address,
        string Footer,
        string? PrinterOption)
    {
        public static Snapshot Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null);
    }
}
