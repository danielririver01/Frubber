using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Domain.Trial;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Trial block dialog logic (JHO-27): explains why the app cannot start
/// (expired trial or backwards clock), offers license activation, or exits.
/// Data is never touched here: the block only gates startup. The host window
/// closes itself through <see cref="Closed"/> (true = licensed, continue).
/// </summary>
public sealed class TrialBlockedViewModel : INotifyPropertyChanged
{
    private readonly GetLicenseStatusUseCase licenseStatus;
    private readonly INavigationService navigation;

    private string message = string.Empty;

    public TrialBlockedViewModel(GetLicenseStatusUseCase licenseStatus, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(licenseStatus);
        ArgumentNullException.ThrowIfNull(navigation);
        this.licenseStatus = licenseStatus;
        this.navigation = navigation;

        ActivateCommand = new AsyncCommand(_ => ActivateAsync());
        ExitCommand = new RelayCommand(() => Closed?.Invoke(this, false));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<bool>? Closed;

    public string Message
    {
        get => message;
        private set => SetProperty(ref message, value);
    }

    public ICommand ActivateCommand { get; }

    public ICommand ExitCommand { get; }

    public void Setup(TrialOutcome outcome)
    {
        Message = outcome switch
        {
            TrialOutcome.Expired => "El periodo de prueba de 30 días terminó. Sus datos están a salvo. "
                + "Active su licencia vitalicia para seguir usando Frubber POS.",
            TrialOutcome.ClockTampered => "Se detectó un retroceso en el reloj del equipo. Corrija la fecha y hora "
                + "e inténtelo de nuevo, o active su licencia vitalicia para continuar. Sus datos están a salvo.",
            _ => "Frubber POS no puede iniciar en este momento. Sus datos están a salvo.",
        };
    }

    public async Task ActivateAsync()
    {
        navigation.ShowLicense();

        var status = await licenseStatus.ExecuteAsync(new GetLicenseStatusRequest(), CancellationToken.None);
        if (status.IsSuccess && status.Value.IsLicensed)
        {
            Closed?.Invoke(this, true);
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
