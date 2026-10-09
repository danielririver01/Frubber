using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Backup;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Respaldo (JHO-53, rediseño JHO-81): admin-only manual backup with a
/// destination chosen by the operator. No restore exists in the product.
/// Phase 1 shell shows live KPIs (database size, last backup, USB off,
/// alert if never backed up) and a health banner.
/// </summary>
public sealed class BackupViewModel : INotifyPropertyChanged
{
    private readonly CreateBackupUseCase createBackup;
    private readonly GetBackupOverviewUseCase getOverview;
    private readonly INavigationService navigation;

    private string databaseSizeText = "0 KB";
    private string lastBackupText = "Nunca";
    private string lastBackupLocation = "Sin respaldos todavía.";
    private string alertCountText = "1";
    private string alertNote = "Todavía no has creado ningún respaldo.";
    private bool isHealthy;
    private string bannerTitle = "Tus datos no tienen respaldo";
    private string bannerSubtitle =
        "Crea tu primer copia de seguridad para proteger ventas, productos y usuarios.";
    private bool busy;
    private string statusMessage = string.Empty;
    private string errorMessage = string.Empty;

    public BackupViewModel(
        CreateBackupUseCase createBackup,
        GetBackupOverviewUseCase getOverview,
        INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(createBackup);
        ArgumentNullException.ThrowIfNull(getOverview);
        ArgumentNullException.ThrowIfNull(navigation);
        this.createBackup = createBackup;
        this.getOverview = getOverview;
        this.navigation = navigation;
        CreateBackupCommand = new AsyncCommand(_ => CreateAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncCommand CreateBackupCommand { get; }

    /// <summary>Live database size, e.g. "128 MB".</summary>
    public string DatabaseSizeText
    {
        get => databaseSizeText;
        private set => Set(ref databaseSizeText, value);
    }

    /// <summary>"No configurada" until USB copy exists in a later phase.</summary>
    public string UsbText => "No configurada";

    public string UsbNote => "Solo respaldo manual en esta versión.";

    public bool HasLastBackup { get; private set; }

    /// <summary>"Nunca", "Hace un momento", "Hace 2 horas", or the file name.</summary>
    public string LastBackupText
    {
        get => lastBackupText;
        private set => Set(ref lastBackupText, value);
    }

    public string LastBackupLocation
    {
        get => lastBackupLocation;
        private set => Set(ref lastBackupLocation, value);
    }

    /// <summary>1 when never backed up (amber banner); otherwise 0.</summary>
    public string AlertCountText
    {
        get => alertCountText;
        private set => Set(ref alertCountText, value);
    }

    public string AlertNote
    {
        get => alertNote;
        private set => Set(ref alertNote, value);
    }

    public bool IsHealthy
    {
        get => isHealthy;
        private set => Set(ref isHealthy, value);
    }

    public string BannerTitle
    {
        get => bannerTitle;
        private set => Set(ref bannerTitle, value);
    }

    public string BannerSubtitle
    {
        get => bannerSubtitle;
        private set => Set(ref bannerSubtitle, value);
    }

    public bool Busy
    {
        get => busy;
        private set => Set(ref busy, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => Set(ref statusMessage, value);
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => Set(ref errorMessage, value);
    }

    public string SaveFolderRowText => "Eliges la carpeta en el diálogo de Windows";

    public string LastRecapText => HasLastBackup ? LastBackupText : "Nunca";

    public async Task CreateAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        Busy = true;
        try
        {
            var destination = navigation.AskBackupDestination();
            if (string.IsNullOrEmpty(destination))
            {
                return;
            }

            var result = await createBackup.ExecuteAsync(
                new CreateBackupRequest(destination), CancellationToken.None);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            StatusMessage = $"Listo: {result.Value.FileName} en {result.Value.DestinationPath}";
            await InitializeAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Refreshes KPIs from the database size and last-backup sidecar (JHO-81).</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = string.Empty;
        var result = await getOverview.ExecuteAsync(new GetBackupOverviewRequest(), cancellationToken);
        if (result.IsFailure)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        Apply(result.Value);
    }

    private void Apply(BackupOverview overview)
    {
        DatabaseSizeText = FormatSize(overview.DatabaseSizeBytes);
        HasLastBackup = overview.LastBackupUtc is not null && !string.IsNullOrWhiteSpace(overview.LastBackupFileName);
        LastBackupText = HasLastBackup
            ? FormatWhen(overview.LastBackupUtc!.Value)
            : "Nunca";
        LastBackupLocation = HasLastBackup
            ? overview.LastBackupPath ?? string.Empty
            : "Sin respaldos todavía.";
        AlertCountText = HasLastBackup ? "0" : "1";
        AlertNote = HasLastBackup
            ? "Sin alertas de respaldo."
            : "Todavía no has creado ningún respaldo.";

        IsHealthy = HasLastBackup;
        BannerTitle = IsHealthy
            ? "Tus datos tienen un respaldo"
            : "Tus datos no tienen respaldo";
        BannerSubtitle = IsHealthy
            ? $"Último respaldo: {LastBackupText}. Crea una copia nueva cuando quieras."
            : "Crea tu primer copia de seguridad para proteger ventas, productos y usuarios.";

        OnPropertyChanged(nameof(DatabaseSizeText));
        OnPropertyChanged(nameof(UsbText));
        OnPropertyChanged(nameof(UsbNote));
        OnPropertyChanged(nameof(HasLastBackup));
        OnPropertyChanged(nameof(LastBackupText));
        OnPropertyChanged(nameof(LastBackupLocation));
        OnPropertyChanged(nameof(AlertCountText));
        OnPropertyChanged(nameof(AlertNote));
        OnPropertyChanged(nameof(IsHealthy));
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(BannerSubtitle));
        OnPropertyChanged(nameof(LastRecapText));
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        double kb = bytes / 1024.0;
        if (kb < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{kb:0.#} KB");
        }

        double mb = kb / 1024.0;
        if (mb < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{mb:0.#} MB");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{mb / 1024.0:0.#} GB");
    }

    public static string FormatWhen(DateTimeOffset utc)
    {
        var local = utc.ToLocalTime();
        var delta = DateTimeOffset.Now - local;
        if (delta < TimeSpan.FromMinutes(1))
        {
            return "Hace un momento";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            var minutes = (int)delta.TotalMinutes;
            return minutes == 1 ? "Hace 1 minuto" : $"Hace {minutes} minutos";
        }

        if (delta < TimeSpan.FromHours(24))
        {
            var hours = (int)delta.TotalHours;
            return hours == 1 ? "Hace 1 hora" : $"Hace {hours} horas";
        }

        var days = (int)delta.TotalDays;
        return days == 1 ? "Ayer" : $"Hace {days} días";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
