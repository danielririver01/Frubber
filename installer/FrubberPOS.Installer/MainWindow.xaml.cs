using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FrubberPOS.Installer;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly string[] StepTitles = ["Bienvenida", "Licencia", "Ubicación", "Instalación", "Listo"];

    private readonly InstallService installer = new();
    private int currentStep = 1;
    private double progressFraction;
    private bool installing;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += OnLoaded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int CurrentStep
    {
        get => currentStep;
        private set
        {
            if (currentStep == value) return;
            currentStep = value;
            // JHO-95: al (re)entrar al paso 4 el botón vuelve a su texto normal;
            // tras un fallo se queda en «Intentar de nuevo» hasta navegar.
            if (value == 4) InstallBtn.Content = "Comenzar instalación →";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentStep)));
            UpdateUi();
        }
    }

    public string ProgressPercent => (int)Math.Round(progressFraction * 100) + "%";

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StateBadgeText.Text = "Sin internet necesario";
        BuildSteps();
        RenderLicense();
        PathBox.Text = InstallService.DefaultInstallDir;
        UpdateUi();
    }

    // ---------- Rail de pasos (01…05, activo/completado, clic solo hacia atrás) ----------

    private void BuildSteps()
    {
        for (var i = 1; i <= StepTitles.Length; i++)
        {
            var stepIndex = i;
            var number = new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x3D, 0xC6, 0xDE, 0xE1)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Margin = new Thickness(0, 0, 13, 0),
            };
            var indexBinding = new Binding(nameof(CurrentStep)) { Source = this };
            var selfNumber = new Binding { Source = stepIndex };
            var multi = new MultiBinding { Converter = (StepStateBrushConverter)FindResource("StepBg") };
            multi.Bindings.Add(indexBinding);
            multi.Bindings.Add(selfNumber);
            number.SetBinding(Border.BackgroundProperty, multi);

            var numberText = (TextBlock)number.Child;
            numberText.SetBinding(TextBlock.TextProperty, new Binding { Source = stepIndex.ToString("D2") });
            var numFg = new MultiBinding { Converter = (StepTextBrushConverter)FindResource("StepText") };
            numFg.Bindings.Add(new Binding(nameof(CurrentStep)) { Source = this });
            numFg.Bindings.Add(new Binding { Source = stepIndex });
            numberText.Foreground = Brushes.White;
            numberText.FontWeight = FontWeights.Bold;
            numberText.FontSize = 11;

            var title = new TextBlock
            {
                Text = StepTitles[stepIndex - 1],
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var fgMulti = new MultiBinding { Converter = (StepTextBrushConverter)FindResource("StepText") };
            fgMulti.Bindings.Add(new Binding(nameof(CurrentStep)) { Source = this });
            fgMulti.Bindings.Add(new Binding { Source = stepIndex });
            title.SetBinding(TextBlock.ForegroundProperty, fgMulti);

            var rowContent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10) };
            rowContent.Children.Add(number);
            rowContent.Children.Add(title);

            var button = new Button
            {
                Content = rowContent,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Tag = stepIndex,
                Padding = new Thickness(6),
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            button.Click += (_, _) =>
            {
                if (installing || stepIndex >= currentStep || currentStep >= 5 || stepIndex == 4) return;
                CurrentStep = stepIndex;
            };
            StepsHost.Children.Add(button);
        }
    }

    // ---------- Licencia (mismo texto del contrato del instalador) ----------

    private void RenderLicense()
    {
        var assembly = typeof(MainWindow).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("Licencia-FrubberPOS.txt", StringComparison.OrdinalIgnoreCase));
        string[] lines;
        if (name is null)
        {
            lines =
            [
                "FRUBBER POS — TÉRMINOS Y CONDICIONES DE LICENCIA DE USO",
                "Al instalar Frubber POS aceptas los términos incluidos con el producto."
            ];
        }
        else
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var readerText = new StreamReader(stream);
            lines = readerText.ReadToEnd().Split(["\r\n", "\n"], StringSplitOptions.None);
        }

        var first = true;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;

            if (first)
            {
                LicenseBody.Children.Add(new TextBlock
                {
                    Text = line,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x35, 0x44)),
                    FontSize = 12, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 15),
                });
                first = false;
            }
            else if (char.IsDigit(line[0]) && line.Contains('.'))
            {
                LicenseBody.Children.Add(new TextBlock
                {
                    Text = line,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x31, 0x53, 0x60)),
                    FontSize = 11, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 13, 0, 4),
                });
            }
            else
            {
                LicenseBody.Children.Add(new TextBlock
                {
                    Text = line,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x65, 0x79, 0x84)),
                    FontSize = 11, TextWrapping = TextWrapping.Wrap, LineHeight = 17,
                });
            }
        }
    }

    // ---------- Navegación ----------

    private void OnAcceptChanged(object sender, RoutedEventArgs e) => UpdateUi();

    private void OnPathChanged(object sender, TextChangedEventArgs e)
    {
        if (CurrentStep == 4) RefreshSummary();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        // 1 Bienvenida → 2 Licencia → 3 Ubicación → 4 Confirmación (4→5 solo instala).
        if (CurrentStep >= 4) return;
        if (CurrentStep == 2 && AcceptCheck.IsChecked != true) return;
        CurrentStep++;
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (CurrentStep <= 1 || CurrentStep >= 5 || installing) return;
        CurrentStep--;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Elige la carpeta de instalación",
            InitialDirectory = InstallService.DefaultInstallDir,
        };
        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FolderName;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (installing) return; // nada de cerrar a mitad de copia
        Close();
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    // ---------- Instalación real ----------

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        var target = PathBox.Text.Trim();
        if (target.Length == 0)
        {
            InstallErrorSuccess("Escribe la carpeta de instalación.");
            return;
        }

        // JHO-95: si eligió la raíz de un disco (F:\), se instala en
        // <disco>:\Frubber POS y la carpeta real queda visible en el resumen.
        target = InstallService.NormalizeTargetDir(target);
        if (!string.Equals(PathBox.Text, target, StringComparison.Ordinal)) PathBox.Text = target;

        installing = true;
        InstallBtn.IsEnabled = false;
        InstallError.Visibility = Visibility.Collapsed;
        ProgressWrap.Visibility = Visibility.Visible;
        ProgressHint.Text = string.Empty;
        UpdateUi();

        var progress = new Progress<(double Fraction, string Stage)>(p =>
        {
            progressFraction = p.Fraction;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressPercent)));
            ProgressFill.Width = Math.Max(8, ProgressFill.Parent is Border track && track.ActualWidth > 0
                ? track.ActualWidth * p.Fraction : 0);
            ProgressLabel.Text = p.Stage;
        });

        try
        {
            await installer.InstallAsync(target, ShortcutCheck.IsChecked == true, progress, CancellationToken.None);
            CurrentStep = 5;
        }
        catch (Exception ex)
        {
            // JHO-95: mensaje según la fase que falló (copiar / accesos / registro)
            // y una salida clara: reintentar o volver para cambiar la carpeta.
            InstallError.Text = ex is InstallFailedException failed
                ? failed.Message
                : ex is IOException or UnauthorizedAccessException
                    ? "No se pudieron copiar los archivos. Cierra Frubber POS si está abierto, revisa la carpeta e intenta de nuevo."
                    : "La instalación no pudo completarse: " + ex.Message;
            InstallError.Visibility = Visibility.Visible;
            ProgressHint.Text = "Puedes usar «Volver» para cambiar la carpeta y reintentar.";
            InstallBtn.Content = "Intentar de nuevo →";
        }
        finally
        {
            installing = false;
            InstallBtn.IsEnabled = true;
            UpdateUi();
        }
    }

    private void OnOpenAppClick(object sender, RoutedEventArgs e)
    {
        var exe = Path.Combine(PathBox.Text.Trim(), InstallService.AppExeName);
        if (File.Exists(exe))
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }

        Close();
    }

    private void InstallErrorSuccess(string message)
    {
        ProgressWrap.Visibility = Visibility.Visible;
        InstallError.Text = message;
        InstallError.Visibility = Visibility.Visible;
    }

    private void RefreshSummary()
    {
        var path = PathBox.Text.Trim();
        SummaryPath.Text = path.Length == 0 ? "Ubicación recomendada" : path;
        SummaryShortcut.Text = ShortcutCheck.IsChecked == true ? "Sí, en el escritorio" : "No";
    }

    private void UpdateUi()
    {
        FooterStep.Text = CurrentStep == 5 ? "Instalación terminada" : $"Paso {CurrentStep} de 5";
        BackBtn.IsEnabled = CurrentStep > 1 && CurrentStep < 5 && !installing;
        NextBtn.Visibility = CurrentStep <= 3 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.IsEnabled = !installing && (CurrentStep != 2 || AcceptCheck.IsChecked == true);
        NextBtn.Content = CurrentStep == 2 ? "Acepto y continúo →" : "Continuar →";
        if (CurrentStep == 4) RefreshSummary();
    }
}
