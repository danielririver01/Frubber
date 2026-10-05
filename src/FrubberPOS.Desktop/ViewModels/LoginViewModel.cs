using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Trial;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Domain.Trial;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>Qué hace Esc en la vista de login según la fase y el modo (JHO-64).</summary>
public enum LoginEscapeAction
{
    /// <summary>Ya se resolvió internamente (fase PIN) o no aplica (modo bloqueo, fase usuario).</summary>
    None,

    /// <summary>Fase de usuario en arranque: la ventana debe cerrar la aplicación.</summary>
    CloseApp
}

/// <summary>
/// Login flow (P-UX §6.5, JHO-64): two phases exactly like the owner's HTML
/// prototype — pick a user, then type the 4-digit PIN. Digits auto-submit at
/// four; a failed attempt shakes the dots, locks the pad for a beat, clears
/// the PIN and keeps the selection. <see cref="IsLockMode"/> renders the lock
/// variant of the same card over the shell (secured session) and
/// <see cref="OnAuthenticated"/> reports the unlock back to the caller.
/// </summary>
public sealed class LoginViewModel : INotifyPropertyChanged
{
    public const int PinLength = 4;

    private readonly ListActiveUsersUseCase listUsers;
    private readonly AuthenticateUserUseCase authenticate;
    private readonly EvaluateTrialUseCase trial;
    private readonly INavigationService navigation;

    private ActiveUserDto? selectedUser;
    private string pin = string.Empty;
    private string errorMessage = string.Empty;
    private string trialLine = string.Empty;
    private string licensePillText = "Sin activar";
    private string successLine = string.Empty;
    private string successName = string.Empty;
    private string welcomeTitle = string.Empty;
    private string welcomeSubtitle = string.Empty;
    private string welcomeInitials = "?";
    private bool pinError;
    private bool isBusy;
    private bool isEntering;
    private bool isPinPhase;
    private bool isLockMode;
    private bool lockBannerVisible;

    public LoginViewModel(
        ListActiveUsersUseCase listUsers,
        AuthenticateUserUseCase authenticate,
        EvaluateTrialUseCase trial,
        INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(listUsers);
        ArgumentNullException.ThrowIfNull(authenticate);
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(navigation);
        this.listUsers = listUsers;
        this.authenticate = authenticate;
        this.trial = trial;
        this.navigation = navigation;

        LoadCommand = new AsyncCommand(_ => LoadUsersAsync());
        TrialCommand = new AsyncCommand(_ => LoadTrialAsync());
        LoginCommand = new AsyncCommand(_ => LoginAsync());
        ShowLicenseCommand = new RelayCommand(() => navigation.ShowLicense());
        EnterDigitCommand = new RelayCommand<string>(EnterDigit);
        BackspaceCommand = new RelayCommand(Backspace);
        GoToPinCommand = new RelayCommand(GoToPin);
        BackToUsersCommand = new RelayCommand(BackToUsers);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Retrasos de animación; los tests los bajan para ser rápidos.</summary>
    internal int ErrorLockMs { get; set; } = 1100;

    internal int OkHoldMs { get; set; } = 550;

    internal int EnteringDelayMs { get; set; } = 1500;

    internal int LockBannerMs { get; set; } = 2000;

    /// <summary>
    /// Sección (arranque): null → navega al shell. Desbloqueo de sesión:
    /// MainWindow la fija para reforzar el usuario y ocultar el overlay.
    /// </summary>
    public Func<SignedInUser, Task>? OnAuthenticated { get; set; }

    public ObservableCollection<ActiveUserDto> Users { get; } = new();

    public ActiveUserDto? SelectedUser
    {
        get => selectedUser;
        set
        {
            if (SetProperty(ref selectedUser, value))
            {
                Pin = string.Empty;
                PinError = false;
                ErrorMessage = string.Empty;
                Raise(nameof(SelectedUserLabel));
                Raise(nameof(ContinueName));
                Raise(nameof(CanContinue));
            }
        }
    }

    /// <summary>Bajo el pad en fase PIN: solo el rol (prototipo lg-2/lg-3).</summary>
    public string SelectedUserLabel => SelectedUser is null
        ? "Seleccione un usuario"
        : SelectedUser.Role == Domain.Users.UserRole.Admin ? "Administrador" : "Cajero";

    /// <summary>Primer nombre del usuario, para el botón "Continuar como …".</summary>
    public string ContinueName
    {
        get
        {
            var name = SelectedUser?.DisplayName ?? string.Empty;
            var space = name.IndexOf(' ');
            return space > 0 ? name[..space] : name;
        }
    }

    public bool CanContinue => SelectedUser is not null;

    public string Pin
    {
        get => pin;
        private set
        {
            if (SetProperty(ref pin, value))
            {
                Raise(nameof(PinMask));
                RaisePinHint();
            }
        }
    }

    public string PinMask => new string('●', Pin.Length).PadRight(PinLength, '○');

    /// <summary>
    /// Guía debajo de los dots en tres trozos —texto, trozo fuerte, texto—
    /// porque el prototipo resalta con &lt;strong&gt; acciones y contadores
    /// (lg-1 "Continuar", lg-3 "Entrar"/"faltan N", lg-5 nombre).
    /// </summary>
    public string PinHintLead => HintParts().lead;

    public string PinHintStrong => HintParts().strong;

    public string PinHintTail => HintParts().tail;

    public string PinHintText => HintParts().lead + HintParts().strong + HintParts().tail;

    private (string lead, string strong, string tail) HintParts()
    {
        if (!string.IsNullOrEmpty(SuccessLine))
        {
            return ("PIN correcto · bienvenido, ", successName, string.Empty);
        }

        if (!IsPinPhase)
        {
            return ("Elige tu usuario y pulsa ", "Continuar", string.Empty);
        }

        if (Pin.Length == 0)
        {
            return ($"Ingresa tu PIN de {PinLength} dígitos", string.Empty, string.Empty);
        }

        if (Pin.Length == PinLength)
        {
            return ("Listo · pulsa ", "Entrar", " para acceder");
        }

        return ($"Ingresa tu PIN de {PinLength} dígitos · faltan ",
            (PinLength - Pin.Length).ToString(), string.Empty);
    }

    /// <summary>Bandera de animación de error (shake + dots rojos del prototipo lg-4).</summary>
    public bool PinError
    {
        get => pinError;
        private set => SetProperty(ref pinError, value);
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetProperty(ref errorMessage, value))
            {
                Raise(nameof(HasErrorMessage));
            }
        }
    }

    /// <summary>El error reemplaza a la guía en línea (prototipo lg-4).</summary>
    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Mensaje en verde "PIN correcto · bienvenido, …" antes del overlay.</summary>
    public string SuccessLine
    {
        get => successLine;
        private set
        {
            if (SetProperty(ref successLine, value))
            {
                RaisePinHint();
                Raise(nameof(HasSuccessLine));
            }
        }
    }

    public bool HasSuccessLine => !string.IsNullOrEmpty(SuccessLine);

    /// <summary>Overlay "Bienvenido, …" durante la transición al sistema.</summary>
    public string WelcomeTitle
    {
        get => welcomeTitle;
        private set => SetProperty(ref welcomeTitle, value);
    }

    public string WelcomeSubtitle
    {
        get => welcomeSubtitle;
        private set => SetProperty(ref welcomeSubtitle, value);
    }

    public string WelcomeInitials
    {
        get => welcomeInitials;
        private set => SetProperty(ref welcomeInitials, value);
    }

    public bool IsEntering
    {
        get => isEntering;
        private set
        {
            if (SetProperty(ref isEntering, value))
            {
                Raise(nameof(KeypadEnabled));
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                Raise(nameof(KeypadEnabled));
            }
        }
    }

    /// <summary>Fase actual: false = lista de usuarios, true = pad PIN.</summary>
    public bool IsPinPhase
    {
        get => isPinPhase;
        private set
        {
            if (SetProperty(ref isPinPhase, value))
            {
                Raise(nameof(IsUserPhase));
                Raise(nameof(KeypadEnabled));
                Raise(nameof(ContinueButtonVisible));
                Raise(nameof(BackUsersVisible));
                Raise(nameof(EnterHintText));
                Raise(nameof(EscapeHintText));
                RaisePinHint();
            }
        }
    }

    public bool IsUserPhase => !IsPinPhase;

    /// <summary>Variante bloqueo de la tarjeta (sesión asegurada, JHO-64).</summary>
    public bool IsLockMode
    {
        get => isLockMode;
        private set
        {
            if (SetProperty(ref isLockMode, value))
            {
                Raise(nameof(EscapeHintText));
            }
        }
    }

    /// <summary>Toast "🔒 Sesión bloqueada por seguridad" al activar el lock.</summary>
    public bool LockBannerVisible
    {
        get => lockBannerVisible;
        private set => SetProperty(ref lockBannerVisible, value);
    }

    /// <summary>El pad solo opera en fase PIN y fuera de animaciones.</summary>
    public bool KeypadEnabled => IsPinPhase && !IsBusy && !IsEntering;

    public bool ContinueButtonVisible => !IsPinPhase;

    public bool BackUsersVisible => IsPinPhase && Users.Count > 1;

    public string EnterHintText => IsPinPhase ? "Entrar" : "Continuar";

    /// <summary>Arranque cambia según la fase; en bloqueo siempre es "volver".</summary>
    public string EscapeHintText =>
        IsLockMode || (IsPinPhase && Users.Count > 1) ? "Volver a la lista" : "Salir";

    public string TrialLine
    {
        get => trialLine;
        private set => SetProperty(ref trialLine, value);
    }

    /// <summary>Insignia única del encabezado (JHO-64): licencia, prueba o sin activar.</summary>
    public string LicensePillText
    {
        get => licensePillText;
        private set => SetProperty(ref licensePillText, value);
    }

    public ICommand LoadCommand { get; }

    public ICommand TrialCommand { get; }

    public ICommand LoginCommand { get; }

    public ICommand ShowLicenseCommand { get; }

    public ICommand EnterDigitCommand { get; }

    public ICommand BackspaceCommand { get; }

    public ICommand GoToPinCommand { get; }

    public ICommand BackToUsersCommand { get; }

    public async Task LoadUsersAsync()
    {
        var result = await listUsers.ExecuteAsync(new ListActiveUsersRequest(), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        Users.Clear();
        foreach (var user in result.Value.Users)
        {
            Users.Add(user);
        }

        // Prototipo: la primera fila queda marcada (sel:0) y el clic entra al PIN.
        if (Users.Count > 0)
        {
            SelectedUser = Users[0];
        }

        SkipSelectionWhenSingleUser();

        ErrorMessage = string.Empty;
        Raise(nameof(EscapeHintText));
    }

    public async Task LoadTrialAsync()
    {
        var result = await trial.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);
        if (!result.IsSuccess)
        {
            TrialLine = string.Empty;
            LicensePillText = "Sin activar";
            return;
        }

        TrialLine = result.Value switch
        {
            { IsLicensed: true } => string.Empty,
            { Outcome: TrialOutcome.Active, DaysLeft: 1 } => "Versión de prueba — queda 1 día.",
            { Outcome: TrialOutcome.Active } => $"Versión de prueba — quedan {result.Value.DaysLeft} días.",
            _ => "La prueba no está disponible en este equipo."
        };
        LicensePillText = result.Value switch
        {
            { IsLicensed: true } => "Licencia vitalicia",
            { Outcome: TrialOutcome.Active } => TrialLine,
            _ => "Sin activar"
        };
    }

    /// <summary>Clic en una fila de la lista: selecciona y pasa al PIN (prototipo).</summary>
    public void SelectAndGoToPin(ActiveUserDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        SelectedUser = user;
        GoToPin();
    }

    public void GoToPin()
    {
        if (IsPinPhase)
        {
            return;
        }

        if (SelectedUser is null)
        {
            ErrorMessage = "Seleccione un usuario.";
            return;
        }

        IsPinPhase = true;
        Pin = string.Empty;
        PinError = false;
        ErrorMessage = string.Empty;
        SuccessLine = string.Empty;
    }

    public void BackToUsers()
    {
        IsPinPhase = false;
        Pin = string.Empty;
        PinError = false;
        ErrorMessage = string.Empty;
        SuccessLine = string.Empty;
        SkipSelectionWhenSingleUser();
    }

    /// <summary>
    /// Un solo usuario activo: no hay nada que elegir, así que la lista se
    /// salta y la tarjeta va directo al PIN (decisión del dueño, JHO-64).
    /// </summary>
    private void SkipSelectionWhenSingleUser()
    {
        if (Users.Count == 1 && SelectedUser is not null && !IsPinPhase)
        {
            GoToPin();
        }
    }

    public void EnterDigit(string? digit)
    {
        if (!KeypadEnabled)
        {
            return;
        }

        if (digit is null || digit.Length != 1 || !char.IsDigit(digit[0]) || Pin.Length >= PinLength)
        {
            return;
        }

        PinError = false;
        Pin += digit;

        // UX: al completar los 4 dígitos se intenta entrar directo,
        // sin exigir Enter (un PIN correcto y uno incorrecto se resuelven igual).
        if (Pin.Length == PinLength && LoginCommand.CanExecute(null))
        {
            LoginCommand.Execute(null);
        }
    }

    public void Backspace()
    {
        if (KeypadEnabled && Pin.Length > 0)
        {
            Pin = Pin[..^1];
        }
    }

    /// <summary>Enter: en usuario pasa al PIN; en PIN intenta entrar.</summary>
    public void HandleEnter()
    {
        if (IsBusy || IsEntering)
        {
            return;
        }

        if (!IsPinPhase)
        {
            GoToPin();
            return;
        }

        if (LoginCommand.CanExecute(null))
        {
            LoginCommand.Execute(null);
        }
    }

    /// <summary>Esc: en PIN vuelve a la lista (salvo con un solo usuario, donde no hay
    /// lista); en usuario cierra (salvo bloqueo).</summary>
    public LoginEscapeAction HandleEscape()
    {
        if (IsPinPhase && Users.Count > 1)
        {
            BackToUsers();
            return LoginEscapeAction.None;
        }

        return IsLockMode ? LoginEscapeAction.None : LoginEscapeAction.CloseApp;
    }

    /// <summary>Activa la variante bloqueo y lanza el toast de 2 segundos.</summary>
    public void ActivateLockMode()
    {
        IsLockMode = true;
        BackToUsers();
        LockBannerVisible = true;
        _ = HideLockBannerAsync();
    }

    public async Task LoginAsync()
    {
        if (IsBusy || IsEntering)
        {
            return;
        }

        if (SelectedUser is null)
        {
            ErrorMessage = "Seleccione un usuario.";
            return;
        }

        if (!IsPinPhase)
        {
            return;
        }

        if (Pin.Length < PinLength)
        {
            ErrorMessage = $"Faltan {PinLength - Pin.Length} dígitos.";
            return;
        }

        IsBusy = true;
        var result = await authenticate.ExecuteAsync(
            new AuthenticateUserRequest(SelectedUser.UserId, Pin), CancellationToken.None);

        if (!result.IsSuccess)
        {
            // Prototipo lg-4: los dots pasan a rojo (la vista anima el shake),
            // el pad queda mudo un instante y luego el PIN se borra.
            if (Pin.Length == PinLength)
            {
                PinError = true;
            }

            Pin = string.Empty;

            // Prototipo lg-4: la pantalla de login muestra su propio texto
            // (el use case mantiene el mensaje anti-enumeración internamente).
            ErrorMessage = result.Error!.Code == ErrorCode.Authentication
                ? "PIN incorrecto · verifica e inténtalo de nuevo"
                : result.Error!.Message;
            await Task.Delay(ErrorLockMs);
            PinError = false;
            IsBusy = false;
            ErrorMessage = string.Empty;
            return;
        }

        await EnterSuccessAsync(
            new SignedInUser(result.Value.UserId, result.Value.DisplayName, result.Value.Role));
    }

    private async Task EnterSuccessAsync(SignedInUser user)
    {
        ErrorMessage = string.Empty;
        successName = user.DisplayName;
        SuccessLine = $"PIN correcto · bienvenido, {user.DisplayName}";
        await Task.Delay(OkHoldMs);

        WelcomeInitials = ComputeInitials(user.DisplayName);
        WelcomeTitle = $"Bienvenido, {user.DisplayName}";
        WelcomeSubtitle = $"{(user.Role == Domain.Users.UserRole.Admin ? "Administrador" : "Cajero")} · Entrando al sistema…";
        IsEntering = true;
        await Task.Delay(EnteringDelayMs);

        SuccessLine = string.Empty;
        IsEntering = false;
        IsBusy = false;
        BackToUsers();

        if (OnAuthenticated is not null)
        {
            await OnAuthenticated(user);
        }
        else
        {
            navigation.ShowShell(user);
        }
    }

    private async Task HideLockBannerAsync()
    {
        await Task.Delay(LockBannerMs);
        LockBannerVisible = false;
    }

    private static string ComputeInitials(string displayName)
    {
        var words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        return initials.Length == 0 ? "?" : initials;
    }

    private void RaisePinHint()
    {
        Raise(nameof(PinHintLead));
        Raise(nameof(PinHintStrong));
        Raise(nameof(PinHintTail));
        Raise(nameof(PinHintText));
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
