using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FrubberPOS.Installer;

/// <summary>
/// Flujo de desinstalación (JHO-93): confirmación clara, progreso real y
/// la regla de oro — los datos del negocio se conservan siempre.
/// </summary>
public partial class UninstallWindow : Window
{
    private readonly InstallService installer = new();
    private bool running;

    public UninstallWindow()
    {
        InitializeComponent();
    }

    private async void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        if (running) return;
        running = true;
        UninstallBtn.IsEnabled = false;
        ProgressWrap.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;

        var target = InstallService.GetInstalledLocation() ?? InstallService.DefaultInstallDir;
        var progress = new Progress<(double Fraction, string Stage)>(p =>
        {
            ProgressLabel.Text = p.Stage;
            ProgressFill.Width = ProgressFill.Parent is Border track && track.ActualWidth > 0
                ? track.ActualWidth * p.Fraction : 0;
        });

        try
        {
            await Task.Run(() => installer.Uninstall(target, progress, CancellationToken.None));
            QuestionText.Text = "Frubber POS se desinstaló. Tus datos del negocio siguen guardados en C:\\ProgramData\\FrubberPOS.";
            Actions.Visibility = Visibility.Collapsed;
            DoneBtn.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ErrorText.Text = "No se pudo desinstalar: " + ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            running = false;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
