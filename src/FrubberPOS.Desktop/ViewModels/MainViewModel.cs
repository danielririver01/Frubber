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
    private readonly Func<object>? productsPageFactory;
    private readonly Func<object>? cashPageFactory;
    private readonly Func<object>? tpvPageFactory;
    private readonly Func<object>? historyPageFactory;
    private readonly Func<object>? settingsPageFactory;
    private readonly Func<object>? backupPageFactory;
    private readonly Func<CancellationToken, Task<bool>>? cashOpenProbe;

    private MenuEntry? selectedEntry;
    private string pageTitle = string.Empty;
    private string pageDescription = string.Empty;
    private object? currentPage;
    private object? productsPage;
    private object? cashPage;
    private object? tpvPage;
    private object? historyPage;
    private object? settingsPage;
    private object? backupPage;
    private bool cashPillVisible;

    public MainViewModel(SignedInUser user, SignOutUseCase signOut, INavigationService navigation, Func<object>? productsPageFactory = null, Func<object>? cashPageFactory = null, Func<object>? tpvPageFactory = null, Func<object>? historyPageFactory = null, Func<object>? settingsPageFactory = null, Func<object>? backupPageFactory = null, string? licenseBadgeText = null, Func<CancellationToken, Task<bool>>? cashOpenProbe = null, bool isLicensed = false)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(signOut);
        ArgumentNullException.ThrowIfNull(navigation);
        this.signOut = signOut;
        this.navigation = navigation;
        this.productsPageFactory = productsPageFactory;
        this.cashPageFactory = cashPageFactory;
        this.tpvPageFactory = tpvPageFactory;
        this.historyPageFactory = historyPageFactory;
        this.settingsPageFactory = settingsPageFactory;
        this.backupPageFactory = backupPageFactory;
        this.cashOpenProbe = cashOpenProbe;

        DisplayName = user.DisplayName;
        IsAdmin = user.Role == UserRole.Admin;
        RoleBadgeText = IsAdmin ? "ADMIN" : "CAJERO";
        UserInitials = ComputeInitials(user.DisplayName);
        LicenseBadgeText = licenseBadgeText;

        MenuItems = new ObservableCollection<MenuEntry>(
            AllEntries.Where(e => !e.AdminOnly || IsAdmin)
                      // Con licencia vitalicia activa el menú Licencia sobra (fig. 1: solo aparece si hay algo que gestionar).
                      .Where(e => e.Title != "Licencia" || !isLicensed));

        NavigateCommand = new RelayCommand(() => ShowSelected());
        SignOutCommand = new AsyncCommand(_ => SignOutAsync());

        SelectedEntry = MenuItems.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; }

    public bool IsAdmin { get; }

    /// <summary>Badge de rol en la barra lateral (diseño aprobado fig. 1).</summary>
    public string RoleBadgeText { get; }

    /// <summary>Iniciales del avatar circular del encabezado.</summary>
    public string UserInitials { get; }

    /// <summary>Texto de la pastilla de licencia del encabezado; null la oculta.</summary>
    public string? LicenseBadgeText { get; }

    public bool LicenseBadgeVisible => !string.IsNullOrEmpty(LicenseBadgeText);

    /// <summary>Pastilla "Caja abierta" del encabezado: se refresca al navegar.</summary>
    public bool CashPillVisible
    {
        get => cashPillVisible;
        private set => SetProperty(ref cashPillVisible, value);
    }

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

    /// <summary>Real page content (JHO-30: products); <c>null</c> shows the placeholder.</summary>
    public object? CurrentPage
    {
        get => currentPage;
        private set => SetProperty(ref currentPage, value);
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
        _ = RefreshBadgesAsync(); // la pastilla de caja refleja el estado al navegar

        if (SelectedEntry?.Title == "Licencia")
        {
            PageTitle = "Licencia";
            PageDescription = "Gestión de la licencia vitalicia.";
            CurrentPage = null;
            navigation.ShowLicense();
            return;
        }

        PageTitle = SelectedEntry?.Title ?? string.Empty;
        if (SelectedEntry?.Title == "Productos" && productsPageFactory is not null)
        {
            PageDescription = string.Empty;
            CurrentPage = productsPage ??= productsPageFactory();
            return;
        }

        if (SelectedEntry?.Title == "Caja" && cashPageFactory is not null)
        {
            PageDescription = string.Empty;
            CurrentPage = cashPage ??= cashPageFactory();
            return;
        }

        if (SelectedEntry?.Title == "Venta" && tpvPageFactory is not null)
        {
            PageDescription = string.Empty;
            CurrentPage = tpvPage ??= tpvPageFactory();
            return;
        }

        if (SelectedEntry?.Title == "Historial" && historyPageFactory is not null)
        {
            PageDescription = string.Empty;
            CurrentPage = historyPage ??= historyPageFactory();
            return;
        }

        if (SelectedEntry?.Title == "Configuración" && settingsPageFactory is not null)
        {
            PageDescription = string.Empty;
            CurrentPage = settingsPage ??= settingsPageFactory();
            return;
        }

        if (SelectedEntry?.Title == "Respaldo" && backupPageFactory is not null)
        {
            PageDescription = string.Empty;
            CurrentPage = backupPage ??= backupPageFactory();
            return;
        }

        CurrentPage = null;
        PageDescription = SelectedEntry is null ? string.Empty : "Módulo en construcción.";
    }

    /// <summary>Relee si hay caja abierta para la pastilla del encabezado.</summary>
    public async Task RefreshBadgesAsync()
    {
        if (cashOpenProbe is null)
        {
            return;
        }

        CashPillVisible = await cashOpenProbe(CancellationToken.None);
    }

    private static string ComputeInitials(string displayName)
    {
        var words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        return initials.Length == 0 ? "?" : initials;
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
