using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Startup;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// First-start wizard: owner name, PIN and confirmation (P-DD-006, JHO-28).
/// PIN match is checked locally; the use case owns the rest.
/// </summary>
public sealed class FirstStartViewModel : INotifyPropertyChanged
{
    private readonly CreateFirstAdministratorUseCase createFirstAdmin;
    private readonly INavigationService navigation;

    private string displayName = string.Empty;
    private string pin = string.Empty;
    private string confirmPin = string.Empty;
    private string errorMessage = string.Empty;

    public FirstStartViewModel(CreateFirstAdministratorUseCase createFirstAdmin, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(createFirstAdmin);
        ArgumentNullException.ThrowIfNull(navigation);
        this.createFirstAdmin = createFirstAdmin;
        this.navigation = navigation;

        CreateCommand = new AsyncCommand(_ => CreateAsync(), CanCreate);
    }

    public bool CanCreate() =>
        !string.IsNullOrWhiteSpace(DisplayName)
        && DisplayName.Trim().Length <= 60
        && Pin.Length == 4
        && Pin == ConfirmPin;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName
    {
        get => displayName;
        set
        {
            if (SetProperty(ref displayName, value))
            {
                CreateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Pin
    {
        get => pin;
        set
        {
            if (SetProperty(ref pin, value))
            {
                CreateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ConfirmPin
    {
        get => confirmPin;
        set
        {
            if (SetProperty(ref confirmPin, value))
            {
                CreateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public AsyncCommand CreateCommand { get; }

    public async Task CreateAsync()
    {
        if (Pin != ConfirmPin)
        {
            ErrorMessage = "Los PIN no coinciden.";
            return;
        }

        var result = await createFirstAdmin.ExecuteAsync(
            new CreateFirstAdministratorRequest(DisplayName, Pin), CancellationToken.None);

        if (!result.IsSuccess)
        {
            Pin = string.Empty;
            ConfirmPin = string.Empty;
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        navigation.ShowLogin();
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
