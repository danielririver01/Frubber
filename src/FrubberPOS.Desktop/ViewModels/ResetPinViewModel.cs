using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// PIN reset dialog (JHO-26, §14.1): only an administrator assigns a new
/// PIN for another user (the old one is never recoverable — we store hash
/// + salt). PIN and confirmation are required and validated by the use
/// case rules (numeric, 4 digits).
/// </summary>
public sealed class ResetPinViewModel : INotifyPropertyChanged
{
    private readonly ResetUserPinUseCase resetPin;

    private UserDetailDto? user;
    private string pin = string.Empty;
    private string confirmPin = string.Empty;
    private string errorMessage = string.Empty;

    public ResetPinViewModel(ResetUserPinUseCase resetPin)
    {
        ArgumentNullException.ThrowIfNull(resetPin);
        this.resetPin = resetPin;
        SaveCommand = new AsyncCommand(_ => SaveAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised to close the dialog; true = PIN restablecido.</summary>
    public event EventHandler<bool>? RequestClose;

    public string UserLabel => user?.DisplayName ?? string.Empty;

    public string Pin
    {
        get => pin;
        set => SetProperty(ref pin, value);
    }

    public string ConfirmPin
    {
        get => confirmPin;
        set => SetProperty(ref confirmPin, value);
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public AsyncCommand SaveCommand { get; }

    public void Load(UserDetailDto userToReset)
    {
        ArgumentNullException.ThrowIfNull(userToReset);
        user = userToReset;
        Pin = string.Empty;
        ConfirmPin = string.Empty;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(UserLabel));
    }

    public async Task SaveAsync()
    {
        if (user is null)
        {
            return;
        }

        if (Pin != ConfirmPin)
        {
            ErrorMessage = "El PIN y la confirmación no coinciden.";
            return;
        }

        var result = await resetPin.ExecuteAsync(
            new ResetUserPinRequest(user.UserId, Pin), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        RequestClose?.Invoke(this, true);
    }

    public void Cancel() => RequestClose?.Invoke(this, false);

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
