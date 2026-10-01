using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Tests.Authentication;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// User editor + PIN reset dialogs (JHO-26, §14.1/14.2): name/role/PIN
/// rules from the use cases, PIN confirmation on create/reset, dialog
/// keeps input on failure.
/// </summary>
public sealed class UserEditorViewModelTests
{
    private static UserEditorViewModel BuildEditor(AuthenticationTestBed bed) =>
        new(
            new CreateUserUseCase(bed.Users, bed.Hasher, bed.Units, AuthenticationTestBed.AdminSession()),
            new UpdateUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession()));

    [Fact]
    public async Task Create_WithMatchingPin_SavesAndCloses()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var vm = BuildEditor(bed);
        vm.Load(null);
        bool? closed = null;
        vm.RequestClose += (_, saved) => closed = saved;

        vm.DisplayName = "Nueva Cajera";
        vm.SelectedRole = UserEditorViewModel.RoleOptions[1];
        vm.Pin = "1234";
        vm.ConfirmPin = "1234";
        await vm.SaveAsync();

        Assert.True(closed);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }

    [Fact]
    public async Task Create_WithMismatchedPin_DoesNotCallUseCase()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var vm = BuildEditor(bed);
        vm.Load(null);
        vm.DisplayName = "Nueva Cajera";
        vm.Pin = "1234";
        vm.ConfirmPin = "4321";

        await vm.SaveAsync();

        Assert.Equal("El PIN y la confirmación no coinciden.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Create_DuplicateName_KeepsInputAndShowsRule()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var vm = BuildEditor(bed);
        vm.Load(null);
        vm.DisplayName = "Admin";
        vm.Pin = "1234";
        vm.ConfirmPin = "1234";

        await vm.SaveAsync();

        Assert.Contains("ya está en uso", vm.ErrorMessage);
        Assert.Equal("Admin", vm.DisplayName); // conserva lo digitado
    }

    [Fact]
    public async Task Edit_ChangesRoleAndName_WithoutAskingPin()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var vm = BuildEditor(bed);
        var existing = new UserDetailDto(2, "Zed Cajero", Domain.Users.UserRole.Cashier, true);
        vm.Load(existing);
        bool? closed = null;
        vm.RequestClose += (_, saved) => closed = saved;

        vm.DisplayName = "Zed Supervisor";
        vm.SelectedRole = UserEditorViewModel.RoleOptions[0];
        await vm.SaveAsync();

        Assert.True(closed);
        Assert.True(vm.IsEditing);
    }

    [Fact]
    public async Task ResetPin_SavesWithConfirmation()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var vm = new ResetPinViewModel(new ResetUserPinUseCase(bed.Users, bed.Hasher, bed.Units, AuthenticationTestBed.AdminSession()));
        vm.Load(new UserDetailDto(2, "Zed Cajero", Domain.Users.UserRole.Cashier, true));
        bool? closed = null;
        vm.RequestClose += (_, saved) => closed = saved;

        Assert.Equal("Zed Cajero", vm.UserLabel);
        vm.Pin = "9999";
        vm.ConfirmPin = "9999";
        await vm.SaveAsync();

        Assert.True(closed);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }
}
