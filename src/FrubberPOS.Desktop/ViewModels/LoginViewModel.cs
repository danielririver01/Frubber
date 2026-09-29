using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Login flow: active-user list, PIN entry, authentication (JHO-24/JHO-29).
/// A failed attempt shows the safe use-case message and clears the PIN.
/// </summary>
public sealed class LoginViewModel : INotifyPropertyChanged
{
    private readonly ListActiveUsersUseCase listUsers;
    private readonly AuthenticateUserUseCase authenticate;
    private readonly INavigationService navigation;

    private ActiveUserDto? selectedUser;
    private string pin = string.Empty;
    private string errorMessage = string.Empty;

    public LoginViewModel(
        ListActiveUsersUseCase listUsers,
        AuthenticateUserUseCase authenticate,
        INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(listUsers);
        ArgumentNullException.ThrowIfNull(authenticate);
        ArgumentNullException.ThrowIfNull(navigation);
        this.listUsers = listUsers;
        this.authenticate = authenticate;
        this.navigation = navigation;

        LoadCommand = new AsyncCommand(_ => LoadUsersAsync());
        LoginCommand = new AsyncCommand(_ => LoginAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ActiveUserDto> Users { get; } = new();

    public ActiveUserDto? SelectedUser
    {
        get => selectedUser;
        set => SetProperty(ref selectedUser, value);
    }

    public string Pin
    {
        get => pin;
        set => SetProperty(ref pin, value);
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public ICommand LoadCommand { get; }

    public ICommand LoginCommand { get; }

    public async Task LoadUsersAsync()
    {
        var result = await listUsers.ExecuteAsync(new ListActiveUsersRequest(), CancellationToken.None);
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

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
