using System.ComponentModel;
using System.Windows;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

public partial class WorkstationWindow : Window
{
    private readonly WorkstationViewModel model;
    private readonly Func<SalesHistoryViewModel> createHistory;
    private readonly Func<ProductsViewModel> createProducts;
    private readonly Func<UsersViewModel> createUsers;

    internal WorkstationWindow(WorkstationViewModel model, Func<SalesHistoryViewModel> createHistory,
        Func<ProductsViewModel> createProducts, Func<UsersViewModel> createUsers)
    {
        this.model = model;
        this.createHistory = createHistory;
        this.createProducts = createProducts;
        this.createUsers = createUsers;
        InitializeComponent();
        DataContext = model;
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        var pin = Pin.Password;
        Pin.Clear();
        await model.SignInAsync(pin);
    }

    private async void OnOpenCash(object sender, RoutedEventArgs e) => await model.OpenCashAsync();
    private void OnHistory(object sender, RoutedEventArgs e) =>
        ShowAdminPage("Historial de ventas", () => new SalesHistoryView(createHistory()));

    private void OnProducts(object sender, RoutedEventArgs e) =>
        ShowAdminPage("Productos", () => new ProductsView(createProducts()));

    private void OnUsers(object sender, RoutedEventArgs e) =>
        ShowAdminPage("Usuarios", () => new UsersView(createUsers()));

    private void ShowAdminPage(string title, Func<FrameworkElement> createView)
    {
        if (!model.ShowAdminMenu)
        {
            return;
        }

        new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 1050,
            Height = 700,
            Content = createView()
        }.ShowDialog();
    }
    private async void OnCloseCash(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfirmActionDialog(
            "Cerrar caja",
            "¿Cerrar la caja de este puesto? El cierre es irreversible y guardará el resumen. (F9 confirma)",
            "Cerrar caja") { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await model.CloseCashAsync();
        }
    }

    private async void OnAddProduct(object sender, RoutedEventArgs e) => await model.AddProductAsync();
    private async void OnCheckout(object sender, RoutedEventArgs e) => await model.CheckoutAsync();
    private async void OnRecoverSale(object sender, RoutedEventArgs e) => await model.RecoverSaleAsync();
    private async void OnAcknowledgeReceipt(object sender, RoutedEventArgs e) => await model.AcknowledgeReceiptAsync();
    private void OnClearSale(object sender, RoutedEventArgs e) => model.ClearSale();
    private void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = !model.CanInteract;
}
