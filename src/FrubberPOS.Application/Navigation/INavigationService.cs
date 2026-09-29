using FrubberPOS.Application.Authentication;

namespace FrubberPOS.Application.Navigation;

/// <summary>
/// Screen-flow contract: first-start, login, shell and license (JHO-29,
/// JHO-25). Declared here (not in Desktop) so ViewModels stay testable
/// without referencing the WPF assembly, per the P-DD §18.4 ViewModel test
/// strategy. Carries no window, dialog or dispatcher types — those live in
/// the Desktop implementation owned by <c>App</c>. ViewModels navigate
/// through this, never through window types.
/// </summary>
public interface INavigationService
{
    void ShowFirstStart();

    void ShowLogin();

    void ShowShell(SignedInUser user);

    void ShowLicense();
}
