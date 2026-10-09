using System.Windows;
using System.Windows.Controls;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>Dumb view for the manual backup page (JHO-53). Loads KPIs once (JHO-81).</summary>
public partial class BackupView : UserControl
{
    private bool loadedOnce;

    public BackupView(BackupViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loadedOnce)
        {
            return;
        }

        loadedOnce = true;
        if (DataContext is BackupViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
}
