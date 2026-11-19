using System.Windows;
using System.Windows.Controls;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

public partial class DailySalesView : UserControl
{
    public DailySalesView(DailySalesViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ((DailySalesViewModel)DataContext).LoadAsync();
    }
}
