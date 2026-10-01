using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// User editor (JHO-26, §14.2): display name (required, ≤ 60, unique),
/// role ADMIN/CASHIER, PIN + confirmation only on creation. The hash never
/// surfaces. Save talks to the use cases; the dialog keeps the input on
/// failure so the operator can correct it.
/// </summary>
public sealed class UserEditorViewModel : INotifyPropertyChanged
{
    private readonly CreateUserUseCase createUser;
    private readonly UpdateUserUseCase updateUser;

    private UserDetailDto? existing;
    private string displayName = string.Empty;
    private string selectedRole;
    private string pin = string.Empty;
    private string confirmPin = string.Empty;
    private string errorMessage = string.Empty;

    public UserEditorViewModel(CreateUserUseCase createUser, UpdateUserUseCase updateUser)
    {
        ArgumentNullException.ThrowIfNull(createUser);
        ArgumentNullException.ThrowIfNull(updateUser);
        this.createUser = createUser;
        this.updateUser = updateUser;
        SaveCommand = new AsyncCommand(_ => SaveAsync());
        selectedRole = RoleOptions[1];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised to close the dialog; true = saved.</summary>
    public event EventHandler<bool>? RequestClose;

    public static IReadOnlyList<string> RoleOptions { get; } = ["Administrador", "Cajero"];

    public string Title { get; private set; } = "Nuevo usuario";

    public bool IsEditing => existing is not null;

    public string DisplayName
    {
        get => displayName;
        set => SetProperty(ref displayName, value);
    }

    public string SelectedRole
    {
        get => selectedRole;
        set => SetProperty(ref selectedRole, value);
    }

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

    public void Load(UserDetailDto? user)
    {
        existing = user;
        Title = user is null ? "Nuevo usuario" : "Editar usuario";
        DisplayName = user?.DisplayName ?? string.Empty;
        SelectedRole = user is null || user.Role == UserRole.Cashier ? RoleOptions[1] : RoleOptions[0];
        Pin = string.Empty;
        ConfirmPin = string.Empty;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsEditing));
    }

    public async Task SaveAsync()
    {
        var role = SelectedRole == RoleOptions[0] ? UserRole.Admin : UserRole.Cashier;

        Application.Common.Result result;
        if (IsEditing)
        {
            result = await updateUser.ExecuteAsync(
                new UpdateUserRequest(existing!.UserId, DisplayName, role), CancellationToken.None);
        }
        else
        {
            if (Pin != ConfirmPin)
            {
                ErrorMessage = "El PIN y la confirmación no coinciden.";
                return;
            }

            result = await createUser.ExecuteAsync(
                new CreateUserRequest(DisplayName, role, Pin), CancellationToken.None);
        }

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
