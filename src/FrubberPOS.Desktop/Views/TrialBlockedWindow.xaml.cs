using System.Windows;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Trial block dialog (JHO-27): expired trial or backwards clock. Owned by
/// <see cref="App"/> at startup; closes itself through the ViewModel's
/// <c>Closed</c> event (true = licensed meanwhile, continue startup).
/// </summary>
public partial class TrialBlockedWindow : Window
{
    private readonly TrialBlockedViewModel viewModel;

    public TrialBlockedWindow(TrialBlockedViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Closed += (_, licensed) =>
        {
            DialogResult = licensed;
            Close();
        };
        InitializeComponent();
    }
}
