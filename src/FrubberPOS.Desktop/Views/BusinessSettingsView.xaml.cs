using System.Windows.Controls;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Business configuration page (JHO-51): the view feeds the installed
/// Windows printers to the ViewModel and loads the saved row once.
/// </summary>
public partial class BusinessSettingsView : UserControl
{
    private bool loadedOnce;

    public BusinessSettingsView(BusinessSettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (loadedOnce)
            {
                return;
            }

            loadedOnce = true;
            await viewModel.InitializeAsync(WindowsTicketPrinter.InstalledPrinters());
        };
    }
}
