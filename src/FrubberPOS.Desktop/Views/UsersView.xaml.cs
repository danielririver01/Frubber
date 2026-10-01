using System.Windows.Controls;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>User administration page (JHO-26): loads once per mount.</summary>
public partial class UsersView : UserControl
{
    private bool loadedOnce;

    public UsersView(UsersViewModel viewModel)
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
            await viewModel.LoadAsync();
        };
    }
}
