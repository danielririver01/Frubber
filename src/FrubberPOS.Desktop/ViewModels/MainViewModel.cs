using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Desktop.ViewModels;

public sealed record MenuEntry(string Title, bool AdminOnly);

/// <summary>
/// Shell: role-filtered menu plus sign-out (JHO-29). Cashiers see only
/// Venta, Caja and sign-out; admins see everything. Destinations are
/// placeholders until their phases build the real pages; the draft
/// lifecycle it must protect arrives with JHO-42.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly MenuEntry[] AllEntries =
    [
        new("Venta", AdminOnly: false),
        new("Caja", AdminOnly: false),
        new("Productos", AdminOnly: true),
        new("Historial", AdminOnly: true),
        new("Usuarios", AdminOnly: true),
        new("Configuración", AdminOnly: true),
        new("Respaldo", AdminOnly: true),
        new("Licencia", AdminOnly: true)
    ];

    private readonly SignOutUseCase signOut;
    private readonly INavigationService navigation;

    private MenuEntry? selectedEntry;
    private string pageTitle = string.Empty;
    private string pageDescription = string.Empty;

    public MainViewModel(SignedInUser user, SignOutUseCase signOut, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(signOut);
        ArgumentNullException.ThrowIfNull(navigation);
        this.signOut = signOut;
        this.navigation = navigation;

        DisplayName = user.DisplayName;
        IsAdmin = user.Role == UserRole.Admin;

        MenuItems = new ObservableCollection<MenuEntry>(
            AllEntries.Where(e => !e.AdminOnly || IsAdmin));

        NavigateCommand = new RelayCommand(() => ShowSelected());
        SignOutCommand = new AsyncCommand(_ => SignOutAsync());

        SelectedEntry = MenuItems.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; }

    public bool IsAdmin { get; }

    public ObservableCollection<MenuEntry> MenuItems { get; }

    public MenuEntry? SelectedEntry
    {
        get => selectedEntry;
        set
        {
            if (SetProperty(ref selectedEntry, value))
            {
                ShowSelected();
            }
        }
    }

    public string PageTitle
    {
        get => pageTitle;
        private set => SetProperty(ref pageTitle, value);
    }

    public string PageDescription
    {
        get => pageDescription;
        private set => SetProperty(ref pageDescription, value);
    }

    public ICommand NavigateCommand { get; }

    public ICommand SignOutCommand { get; }

    public async Task SignOutAsync()
    {
        await signOut.ExecuteAsync(new SignOutRequest(), CancellationToken.None);
        navigation.ShowLogin();
    }

    private void ShowSelected()
    {
        PageTitle = SelectedEntry?.Title ?? string.Empty;
        PageDescription = SelectedEntry is null ? string.Empty : "Módulo en construcción.";
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
