using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// License activation dialog logic (JHO-25): shows the copyable team code,
/// pastes the owner-issued key, and reports "Licencia vitalicia" once the
/// use cases confirm it. Only safe use-case messages ever reach the UI —
/// full keys and hardware serials never appear here.
/// </summary>
public sealed class LicenseActivationViewModel : INotifyPropertyChanged
{
    private readonly GetMachineCodeUseCase machineCode;
    private readonly ActivateLicenseUseCase activate;
    private readonly GetLicenseStatusUseCase status;

    private string machineDisplayCode = string.Empty;
    private string licenseText = string.Empty;
    private string statusText = "Sin activar";
    private string statusLine = "Estado: Sin activar";
    private string confirmationMessage = string.Empty;
    private string errorMessage = string.Empty;
    private bool isLicensed;

    public LicenseActivationViewModel(
        GetMachineCodeUseCase machineCode,
        ActivateLicenseUseCase activate,
        GetLicenseStatusUseCase status)
    {
        ArgumentNullException.ThrowIfNull(machineCode);
        ArgumentNullException.ThrowIfNull(activate);
        ArgumentNullException.ThrowIfNull(status);
        this.machineCode = machineCode;
        this.activate = activate;
        this.status = status;

        LoadCommand = new AsyncCommand(_ => LoadAsync());
        ActivateCommand = new AsyncCommand(_ => ActivateAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string MachineDisplayCode
    {
        get => machineDisplayCode;
        private set => SetProperty(ref machineDisplayCode, value);
    }

    public string LicenseText
    {
        get => licenseText;
        set => SetProperty(ref licenseText, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string StatusLine
    {
        get => statusLine;
        private set => SetProperty(ref statusLine, value);
    }

    public string ConfirmationMessage
    {
        get => confirmationMessage;
        private set => SetProperty(ref confirmationMessage, value);
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public bool IsLicensed
    {
        get => isLicensed;
        private set => SetProperty(ref isLicensed, value);
    }

    public ICommand LoadCommand { get; }

    public ICommand ActivateCommand { get; }

    public async Task LoadAsync()
    {
        ConfirmationMessage = string.Empty;
        ErrorMessage = string.Empty;

        var code = await machineCode.ExecuteAsync(new GetMachineCodeRequest(), CancellationToken.None);
        if (!code.IsSuccess)
        {
            ErrorMessage = code.Error!.Message;
            return;
        }

        MachineDisplayCode = code.Value.DisplayCode;

        var current = await status.ExecuteAsync(new GetLicenseStatusRequest(), CancellationToken.None);
        if (!current.IsSuccess)
        {
            ErrorMessage = current.Error!.Message;
            return;
        }

        ApplyStatus(current.Value.IsLicensed);
    }

    public async Task ActivateAsync()
    {
        ConfirmationMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(LicenseText))
        {
            ErrorMessage = "Pegue el código de licencia que recibió.";
            return;
        }

        var result = await activate.ExecuteAsync(
            new ActivateLicenseRequest(LicenseText.Trim()), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        LicenseText = string.Empty;
        ErrorMessage = string.Empty;
        ApplyStatus(true);
        ConfirmationMessage = "Licencia activada correctamente.";
    }

    private void ApplyStatus(bool licensed)
    {
        IsLicensed = licensed;
        StatusText = licensed ? "Licencia vitalicia" : "Sin activar";
        StatusLine = $"Estado: {StatusText}";
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
