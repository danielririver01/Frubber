using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Tests.Desktop;

public sealed class CommandTests
{
    [Fact]
    public async Task AsyncCommand_BlocksReentryWhileRunning()
    {
        var gate = new TaskCompletionSource();
        var invocations = 0;
        var command = new AsyncCommand(async _ =>
        {
            invocations++;
            await gate.Task;
        });

        command.Execute(null);
        Assert.Equal(1, invocations);
        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));

        command.Execute(null);
        Assert.Equal(1, invocations);

        gate.SetResult();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (command.IsRunning && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.False(command.IsRunning);
        Assert.Equal(1, invocations);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void RelayCommand_ExecutesWhenAllowed()
    {
        var ran = 0;
        var command = new RelayCommand(() => ran++);

        Assert.True(command.CanExecute(null));
        command.Execute(null);

        Assert.Equal(1, ran);
    }

    [Fact]
    public void RelayCommand_RespectsCanExecute()
    {
        var ran = 0;
        var command = new RelayCommand(() => ran++, () => false);

        Assert.False(command.CanExecute(null));
    }
}
