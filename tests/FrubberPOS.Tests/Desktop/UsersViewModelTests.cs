using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Tests.Authentication;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// User administration screen (JHO-26, §14.1): list load, editor/reset
/// orchestration through navigation, destructive confirmations that name
/// the user, and rule enforcement from the use cases.
/// </summary>
public sealed class UsersViewModelTests
{
    private static (UsersViewModel vm, FakeNavigator nav) Build(AuthenticationTestBed bed)
    {
        var nav = new FakeNavigator();
        return (new UsersViewModel(
            new ListUsersUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession()),
            new SetUserActiveStateUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession()),
            new DeleteUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession()),
            nav), nav);
    }

    [Fact]
    public async Task Load_ListsUsersWithRoleAndState()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var (vm, _) = Build(bed);

        await vm.LoadAsync();

        Assert.Equal(4, vm.Users.Count);
        var admin = vm.Users.Single(u => u.DisplayName == "Admin");
        Assert.Equal("Administrador", admin.RoleText);
        Assert.Equal("Activo", admin.StateText);
        Assert.Equal("A", admin.Initials);
        Assert.Equal("Inactivo", vm.Users.Single(u => u.DisplayName == "Inactivo").StateText);
    }

    [Fact]
    public async Task NewUser_SavedEditor_Reloads()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var (vm, nav) = Build(bed);
        await vm.LoadAsync();

        nav.NextUserEditorSaved = true;
        await vm.NewAsync();

        Assert.Equal(1, nav.ShowUserEditorCount);
        Assert.Null(nav.LastEditedUser); // crear
        Assert.Equal("Usuario creado.", vm.StatusMessage);
    }

    [Fact]
    public async Task ResetPin_Saved_ShowsNamedConfirmation()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var (vm, nav) = Build(bed);
        await vm.LoadAsync();
        vm.SelectedUser = vm.Users.Single(u => u.DisplayName == "Zed Cajero");

        nav.NextPinResetSaved = true;
        await vm.ResetPinAsync();

        Assert.Equal(1, nav.ShowPinResetCount);
        Assert.Equal("PIN restablecido para Zed Cajero.", vm.StatusMessage);
    }

    [Fact]
    public async Task DeactivateConfirmed_DeactivatesAndReports()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var (vm, nav) = Build(bed);
        await vm.LoadAsync();
        vm.SelectedUser = vm.Users.Single(u => u.DisplayName == "Zed Cajero");

        nav.NextConfirmResult = true;
        await vm.ToggleActiveAsync();

        Assert.Equal(1, nav.ConfirmUserDeactivationCount);
        Assert.Equal("«Zed Cajero» desactivado.", vm.StatusMessage);
        Assert.Equal("Inactivo", vm.Users.Single(u => u.DisplayName == "Zed Cajero").StateText);
    }

    [Fact]
    public async Task DeactivateLastAdmin_ShowsRuleError()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var (vm, nav) = Build(bed);
        await vm.LoadAsync();
        vm.SelectedUser = vm.Users.Single(u => u.DisplayName == "Admin");

        nav.NextConfirmResult = true;
        await vm.ToggleActiveAsync();

        Assert.Contains("administrador", vm.ErrorMessage);
        Assert.Equal("Activo", vm.Users.Single(u => u.DisplayName == "Admin").StateText);
    }

    [Fact]
    public async Task DeleteCanceled_TouchesNothing()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var (vm, nav) = Build(bed);
        await vm.LoadAsync();
        vm.SelectedUser = vm.Users.Single(u => u.DisplayName == "Zed Cajero");

        nav.NextConfirmResult = false;
        await vm.DeleteAsync();

        Assert.Equal(1, nav.ConfirmUserDeletionCount);
        Assert.Equal(4, vm.Users.Count);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }
}
