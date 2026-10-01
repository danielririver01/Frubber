using System.Windows.Controls;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>Dumb view for the manual backup page (JHO-53).</summary>
public partial class BackupView : UserControl
{
    public BackupView(BackupViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        InitializeComponent();
    }
}
