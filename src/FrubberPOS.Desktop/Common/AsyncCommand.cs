using System.Windows.Input;

namespace FrubberPOS.Desktop.Common;

/// <summary>
/// Async <see cref="ICommand"/> (P-DD §10.2): blocks re-entry while running
/// and exposes <see cref="IsRunning"/> for spinners. <c>Execute</c> is
/// <c>async void</c> because the interface demands it — the single allowed
/// exception to the rule; failures must be observed inside the delegate.
/// </summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<CancellationToken, Task> execute;
    private readonly Func<bool>? canExecute;
    private bool running;

    public AsyncCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => running;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) => !running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute(CancellationToken.None);
        }
        finally
        {
            running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
