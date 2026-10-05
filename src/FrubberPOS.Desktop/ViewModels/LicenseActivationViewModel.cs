using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Domain.Licensing;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// License activation dialog logic (JHO-25, rediseño JHO-64): shows the
/// copyable team code, pastes the owner-issued key, and reports
/// "Licencia vitalicia" once the use cases confirm it. Only safe use-case
/// messages ever reach the UI — full keys and hardware serials never
/// appear here. The owner's prototype (frubber-pos-configuracion.html
/// lic-*) drives the panel flow: input error state, busy spinner, success
/// panel, discard confirmation and the copy feedback label.
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
    private string copyLabel = "Copiar";
    private bool isLicensed;
    private bool inputError;
    private bool isBusy;
    private bool showSuccess;
    private bool showDiscardConfirm;

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
        ActivateCommand = new AsyncCommand(_ => ActivateAsync(), () => CanActivate);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Retraso de la etiqueta "Copiado"; los tests lo bajan.</summary>
    internal int CopyResetMs { get; set; } = 2000;

    public string MachineDisplayCode
    {
        get => machineDisplayCode;
        private set => SetProperty(ref machineDisplayCode, value);
    }

    public string LicenseText
    {
        get => licenseText;
        set
        {
            if (SetProperty(ref licenseText, value))
            {
                InputError = false;
                ErrorMessage = string.Empty;
                Raise(nameof(HasText));
                Raise(nameof(CanActivate));
                ActivateCommand.RaiseCanExecuteChanged();
            }
        }
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

    /// <summary>Borde rojo del input: la clave pasó la forma pero el use case la rechazó.</summary>
    public bool InputError
    {
        get => inputError;
        private set => SetProperty(ref inputError, value);
    }

    /// <summary>La línea de ayuda solo se ve sin error (prototipo licInputState).</summary>
    public bool ShowHelp => !InputError;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                Raise(nameof(ShowHelp));
                Raise(nameof(CanActivate));
                Raise(nameof(ActivateButtonContent));
                ActivateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ShowSuccess
    {
        get => showSuccess;
        private set => SetProperty(ref showSuccess, value);
    }

    public bool ShowDiscardConfirm
    {
        get => showDiscardConfirm;
        private set => SetProperty(ref showDiscardConfirm, value);
    }

    /// <summary>Botón activo: clave con forma válida (dominio) y sin actividad en curso.</summary>
    public bool CanActivate => !IsBusy && LicenseKeyFormat.TryParse(LicenseText, out _);

    /// <summary>Para el placeholder "Pega aquí el código…" del input.</summary>
    public bool HasText => LicenseText.Length > 0;

    public string ActivateButtonContent => IsBusy ? "Activando…" : "Activar licencia";

    public string CopyLabel
    {
        get => copyLabel;
        private set => SetProperty(ref copyLabel, value);
    }

    public ICommand LoadCommand { get; }

    public AsyncCommand ActivateCommand { get; }

    public async Task LoadAsync()
    {
        ConfirmationMessage = string.Empty;
        ErrorMessage = string.Empty;
        ShowSuccess = false;
        ShowDiscardConfirm = false;

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

        IsBusy = true;
        try
        {
            var result = await activate.ExecuteAsync(
                new ActivateLicenseRequest(LicenseText.Trim()), CancellationToken.None);
            if (!result.IsSuccess)
            {
                InputError = true;
                ErrorMessage = result.Error!.Message;
                return;
            }

            LicenseText = string.Empty;
            ErrorMessage = string.Empty;
            InputError = false;
            ApplyStatus(true);
            ConfirmationMessage = "Licencia activada correctamente.";
            ShowSuccess = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Cierre pedido (Esc, ✕, Cancelar, clic en el fondo): true si se cierra ya.</summary>
    public bool TryRequestClose()
    {
        if (ShowDiscardConfirm || IsBusy)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(LicenseText))
        {
            ShowDiscardConfirm = true;
            return false;
        }

        return true;
    }

    public void KeepEditing() => ShowDiscardConfirm = false;

    public void DiscardCode()
    {
        LicenseText = string.Empty;
        InputError = false;
        ErrorMessage = string.Empty;
        ShowDiscardConfirm = false;
    }

    public void NotifyCopied() => _ = CopyResetAsync();

    private async Task CopyResetAsync()
    {
        CopyLabel = "Copiado";
        await Task.Delay(CopyResetMs);
        CopyLabel = "Copiar";
    }

    private void ApplyStatus(bool licensed)
    {
        IsLicensed = licensed;
        StatusText = licensed ? "Licencia vitalicia" : "Sin activar";
        StatusLine = $"Estado: {StatusText}";
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
