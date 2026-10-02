using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Desktop.ViewModels;
/// <summary>
/// Ítem del menú lateral (prototipo integrado, JHO-69): letra-chip, título,
/// filtro por rol y badge de conteo (p. ej. líneas del borrador en Venta).
/// </summary>
public sealed class MenuEntry : INotifyPropertyChanged
{
    private int badgeCount;

    public MenuEntry(string title, string icon, bool adminOnly)
    {
        Title = title;
        Icon = icon;
        AdminOnly = adminOnly;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public string Icon { get; }

    public bool AdminOnly { get; }

    /// <summary>Conteo mostrado en la píldora teal del ítem; 0 = oculta.</summary>
    public int BadgeCount
    {
        get => badgeCount;
        set
        {
            if (value == badgeCount)
            {
                return;
            }

            badgeCount = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BadgeCount)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BadgeVisible)));
        }
    }

    public bool BadgeVisible => BadgeCount > 0;
}

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
        new("Venta", "V", adminOnly: false),
        new("Caja", "C", adminOnly: false),
        new("Productos", "P", adminOnly: true),
        new("Historial", "H", adminOnly: true),
        new("Usuarios", "U", adminOnly: true),
        new("Configuración", "G", adminOnly: true),
        new("Respaldo", "S", adminOnly: true),
        new("Licencia", "L", adminOnly: true)
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
    private readonly Func<object>? usersPageFactory;

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
    private object? usersPage;
    private bool cashPillVisible;

    public MainViewModel(SignedInUser user, SignOutUseCase signOut, INavigationService navigation, Func<object>? productsPageFactory = null, Func<object>? cashPageFactory = null, Func<object>? tpvPageFactory = null, Func<object>? historyPageFactory = null, Func<object>? settingsPageFactory = null, Func<object>? backupPageFactory = null, string? licenseBadgeText = null, Func<CancellationToken, Task<bool>>? cashOpenProbe = null, bool isLicensed = false, Func<object>? usersPageFactory = null, TpvViewModel? draftBadgeSource = null)
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
        this.usersPageFactory = usersPageFactory;

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

        // Badge del ítem Venta = líneas del borrador (prototipo: píldora teal).
        if (draftBadgeSource is not null)
        {
            UpdateVentaBadge(draftBadgeSource.Lines.Count);
            draftBadgeSource.Lines.CollectionChanged += (_, _) => UpdateVentaBadge(draftBadgeSource.Lines.Count);
        }

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

    /// <summary>Trail badge = ámbar; licencia vitalicia = menta (DESIGN.md).</summary>
    public bool LicenseBadgeIsTrial =>
        LicenseBadgeText?.Contains("Prueba", StringComparison.OrdinalIgnoreCase) == true;

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

        if (SelectedEntry?.Title == "Usuarios" && usersPageFactory is not null)
        {
            PageDescription = string.Empty;
            CurrentPage = usersPage ??= usersPageFactory();
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

    /// <summary>Badge del menú Venta = número de líneas en preparación (JHO-69).</summary>
    private void UpdateVentaBadge(int count)
    {
        var venta = MenuItems.FirstOrDefault(e => e.Title == "Venta");
        if (venta is not null)
        {
            venta.BadgeCount = count;
        }
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
