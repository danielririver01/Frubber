namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Ventana de carga (JHO-69): se muestra al iniciar, reporta la fase real
/// del arranque y se cierra antes de abrir login/primer inicio. El diseño
/// replica splash_screen_frubber_pos.html; la animación del ecualizador
/// vive en el XAML (Storyboard), reacciones sin lógica extra.
/// </summary>
public partial class SplashWindow : System.Windows.Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>Actualiza el texto de fase (seguro de llamar desde el hilo UI tras Show()).</summary>
    public void SetStatus(string status)
    {
        StatusText.Text = status;
    }
}
