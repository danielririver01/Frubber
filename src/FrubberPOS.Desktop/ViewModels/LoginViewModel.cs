using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Trial;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Domain.Trial;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Login flow (P-UX §6.5, JHO-29): active-user list plus numeric PIN pad.
/// Digits accumulate to four; Entrar authenticates. A failed attempt shows
/// the safe use-case message, clears the PIN and keeps the selection.
/// </summary>
public sealed class LoginViewModel : INotifyPropertyChanged
{
    public const int PinLength = 4;

    private readonly ListActiveUsersUseCase listUsers;
    private readonly AuthenticateUserUseCase authenticate;
    private readonly EvaluateTrialUseCase trial;
    private readonly INavigationService navigation;

    private ActiveUserDto? selectedUser;
    private string pin = string.Empty;
    private string errorMessage = string.Empty;
    private string trialLine = string.Empty;

    public LoginViewModel(
        ListActiveUsersUseCase listUsers,
        AuthenticateUserUseCase authenticate,
        EvaluateTrialUseCase trial,
        INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(listUsers);
        ArgumentNullException.ThrowIfNull(authenticate);
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(navigation);
        this.listUsers = listUsers;
        this.authenticate = authenticate;
        this.trial = trial;
        this.navigation = navigation;

        LoadCommand = new AsyncCommand(_ => LoadUsersAsync());
        TrialCommand = new AsyncCommand(_ => LoadTrialAsync());
        LoginCommand = new AsyncCommand(_ => LoginAsync());
        ShowLicenseCommand = new RelayCommand(() => navigation.ShowLicense());
        EnterDigitCommand = new RelayCommand<string>(EnterDigit);
        BackspaceCommand = new RelayCommand(Backspace);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ActiveUserDto> Users { get; } = new();

    public ActiveUserDto? SelectedUser
    {
        get => selectedUser;
        set
        {
            if (SetProperty(ref selectedUser, value))
            {
                Pin = string.Empty;
            }
        }
    }

    public string Pin
    {
        get => pin;
        private set
        {
            if (SetProperty(ref pin, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinMask)));
            }
        }
    }

    public string PinMask => new string('●', Pin.Length).PadRight(PinLength, '○');

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public string TrialLine
    {
        get => trialLine;
        private set => SetProperty(ref trialLine, value);
    }

    public ICommand LoadCommand { get; }

    public ICommand TrialCommand { get; }

    public ICommand LoginCommand { get; }

    public ICommand ShowLicenseCommand { get; }

    public ICommand EnterDigitCommand { get; }

    public ICommand BackspaceCommand { get; }

    public async Task LoadUsersAsync()
    {        var result = await listUsers.ExecuteAsync(new ListActiveUsersRequest(), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        Users.Clear();
        foreach (var user in result.Value.Users)
        {
            Users.Add(user);
        }

        ErrorMessage = string.Empty;
    }

    public async Task LoadTrialAsync()
    {
        var result = await trial.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);
        if (!result.IsSuccess)
        {
            TrialLine = string.Empty;
            return;
        }

        TrialLine = result.Value switch
        {
            { IsLicensed: true } => string.Empty,
            { Outcome: TrialOutcome.Active, DaysLeft: 1 } => "Versión de prueba — queda 1 día.",
            { Outcome: TrialOutcome.Active } => $"Versión de prueba — quedan {result.Value.DaysLeft} días.",
            _ => "La prueba no está disponible en este equipo."
        };
    }

    public void EnterDigit(string? digit)
    {
        if (digit is null || digit.Length != 1 || !char.IsDigit(digit[0]) || Pin.Length >= PinLength)
        {
            return;
        }

        Pin += digit;
    }

    public void Backspace()
    {
        if (Pin.Length > 0)
        {
            Pin = Pin[..^1];
        }
    }

    public async Task LoginAsync()
    {
        if (SelectedUser is null)
        {
            ErrorMessage = "Seleccione un usuario.";
            return;
        }

        var result = await authenticate.ExecuteAsync(
            new AuthenticateUserRequest(SelectedUser.UserId, Pin), CancellationToken.None);

        if (!result.IsSuccess)
        {
            Pin = string.Empty;
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        navigation.ShowShell(new SignedInUser(result.Value.UserId, result.Value.DisplayName, result.Value.Role));
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
