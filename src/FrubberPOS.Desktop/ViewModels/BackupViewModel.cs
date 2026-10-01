using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Backup;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Manual backup page (JHO-53, P-UX §16): the operator picks the
/// destination with the standard Windows dialog; while the copy runs a
/// busy indicator shows. Success names file and location; failure states
/// the original database was not altered (plus diagnostic code). There is
/// no restore button — restoring is never offered from the UI.
/// </summary>
public sealed class BackupViewModel : INotifyPropertyChanged
{
    private readonly CreateBackupUseCase createBackup;
    private readonly INavigationService navigation;

    private string statusMessage = string.Empty;
    private string errorMessage = string.Empty;
    private bool busy;

    public BackupViewModel(CreateBackupUseCase createBackup, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(createBackup);
        ArgumentNullException.ThrowIfNull(navigation);
        this.createBackup = createBackup;
        this.navigation = navigation;
        CreateBackupCommand = new AsyncCommand(_ => CreateAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    /// <summary>Indeterminate progress while the media copy runs (§16.1).</summary>
    public bool Busy
    {
        get => busy;
        private set => SetProperty(ref busy, value);
    }

    public AsyncCommand CreateBackupCommand { get; }

    public async Task CreateAsync()
    {
        var destination = navigation.AskBackupDestination();
        if (destination is null)
        {
            return; // el operador canceló el diálogo: sin mensajes
        }

        Busy = true;
        try
        {
            var result = await createBackup.ExecuteAsync(new CreateBackupRequest(destination), CancellationToken.None);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error!.Message;
                StatusMessage = string.Empty;
                return;
            }

            ErrorMessage = string.Empty;
            StatusMessage = $"Respaldo creado: {result.Value.FileName}{Environment.NewLine}{result.Value.DestinationPath}";
        }
        finally
        {
            Busy = false;
        }
    }

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
