using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Navigation;

namespace FrubberPOS.Tests.TestDoubles;

/// <summary>
/// Records navigation calls for ViewModel tests. Production navigation is
/// owned by <c>App</c>.
/// </summary>
public sealed class FakeNavigator : INavigationService
{
    public int FirstStartCount { get; private set; }

    public int LoginCount { get; private set; }

    public int ShellCount { get; private set; }

    public int LicenseCount { get; private set; }

    public SignedInUser? LastShellUser { get; private set; }

    public void ShowFirstStart() => FirstStartCount++;

    public void ShowLogin() => LoginCount++;

    public void ShowLicense() => LicenseCount++;

    public void ShowShell(SignedInUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        ShellCount++;
        LastShellUser = user;
    }
}
