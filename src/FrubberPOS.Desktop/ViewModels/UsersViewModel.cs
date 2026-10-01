using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// User administration page (JHO-26, P-UX §14.1): list with name, role and
/// state; actions Nuevo, Editar, Restablecer PIN, Activar/Desactivar and
/// Eliminar. Rules (unique name, last-admin protection, deactivate instead
/// of delete when there is history) live in the use cases — never here.
/// </summary>
public sealed class UsersViewModel : INotifyPropertyChanged
{
    private readonly ListUsersUseCase listUsers;
    private readonly SetUserActiveStateUseCase setActive;
    private readonly DeleteUserUseCase deleteUser;
    private readonly INavigationService navigation;
    private readonly ObservableCollection<UserRowViewModel> users = [];

    private UserRowViewModel? selectedUser;
    private string statusMessage = string.Empty;
    private string errorMessage = string.Empty;

    public UsersViewModel(
        ListUsersUseCase listUsers,
        SetUserActiveStateUseCase setActive,
        DeleteUserUseCase deleteUser,
        INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(listUsers);
        ArgumentNullException.ThrowIfNull(setActive);
        ArgumentNullException.ThrowIfNull(deleteUser);
        ArgumentNullException.ThrowIfNull(navigation);
        this.listUsers = listUsers;
        this.setActive = setActive;
        this.deleteUser = deleteUser;
        this.navigation = navigation;

        LoadCommand = new AsyncCommand(_ => LoadAsync());
        NewCommand = new AsyncCommand(_ => NewAsync());
        EditCommand = new AsyncCommand(_ => EditAsync(), () => SelectedUser is not null);
        ResetPinCommand = new AsyncCommand(_ => ResetPinAsync(), () => SelectedUser is not null);
        ToggleActiveCommand = new AsyncCommand(_ => ToggleActiveAsync(), () => SelectedUser is not null);
        DeleteCommand = new AsyncCommand(_ => DeleteAsync(), () => SelectedUser is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<UserRowViewModel> Users => users;

    public UserRowViewModel? SelectedUser
    {
        get => selectedUser;
        set
        {
            if (SetProperty(ref selectedUser, value))
            {
                EditCommand.RaiseCanExecuteChanged();
                ResetPinCommand.RaiseCanExecuteChanged();
                ToggleActiveCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

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

    public AsyncCommand LoadCommand { get; }

    public AsyncCommand NewCommand { get; }

    public AsyncCommand EditCommand { get; }

    public AsyncCommand ResetPinCommand { get; }

    public AsyncCommand ToggleActiveCommand { get; }

    public AsyncCommand DeleteCommand { get; }

    public async Task LoadAsync()
    {
        var result = await listUsers.ExecuteAsync(new ListUsersRequest(), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        var selectedId = SelectedUser?.UserId;
        users.Clear();
        foreach (var user in result.Value.Users)
        {
            users.Add(UserRowViewModel.From(user));
        }

        SelectedUser = users.FirstOrDefault(u => u.UserId == selectedId) ?? users.FirstOrDefault();
    }

    public async Task NewAsync()
    {
        if (navigation.ShowUserEditor(null))
        {
            StatusMessage = "Usuario creado.";
            await LoadAsync();
        }
    }

    public async Task EditAsync()
    {
        var selected = SelectedUser;
        if (selected is null)
        {
            return;
        }

        if (navigation.ShowUserEditor(selected.Source))
        {
            StatusMessage = "Usuario actualizado.";
            await LoadAsync();
        }
    }

    public async Task ResetPinAsync()
    {
        var selected = SelectedUser;
        if (selected is null)
        {
            return;
        }

        if (navigation.ShowPinReset(selected.Source))
        {
            StatusMessage = $"PIN restablecido para {selected.DisplayName}.";
            await LoadAsync();
        }
    }

    public async Task ToggleActiveAsync()
    {
        var selected = SelectedUser;
        if (selected is null)
        {
            return;
        }

        var activating = !selected.IsActive;
        if (!activating && !navigation.ConfirmUserDeactivation(selected.Source))
        {
            return;
        }

        var result = await setActive.ExecuteAsync(
            new SetUserActiveStateRequest(selected.UserId, activating), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        StatusMessage = activating ? $"«{selected.DisplayName}» activado." : $"«{selected.DisplayName}» desactivado.";
        await LoadAsync();
    }

    public async Task DeleteAsync()
    {
        var selected = SelectedUser;
        if (selected is null)
        {
            return;
        }

        if (!navigation.ConfirmUserDeletion(selected.Source))
        {
            return;
        }

        var result = await deleteUser.ExecuteAsync(new DeleteUserRequest(selected.UserId), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        StatusMessage = $"«{selected.DisplayName}» eliminado o desactivado según su historial.";
        await LoadAsync();
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

/// <summary>User row (§14.1): name, role, state with explicit text + color.</summary>
public sealed record UserRowViewModel(
    UserDetailDto Source,
    string Initials,
    string DisplayName,
    string RoleText,
    bool IsActive,
    string StateText,
    Brush StateBrush)
{
    public int UserId => Source.UserId;

    public static UserRowViewModel From(UserDetailDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var words = dto.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        return new UserRowViewModel(
            dto,
            initials.Length == 0 ? "?" : initials,
            dto.DisplayName,
            dto.Role == UserRole.Admin ? "Administrador" : "Cajero",
            dto.IsActive,
            dto.IsActive ? "Activo" : "Inactivo",
            dto.IsActive
                ? new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D))
                : new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C)));
    }
}
