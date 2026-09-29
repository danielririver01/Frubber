using System.Windows.Input;

namespace FrubberPOS.Desktop.Common;

/// <summary>
/// Synchronous <see cref="ICommand"/> for UI-only steps. Anything touching
/// ports goes through <see cref="AsyncCommand"/>.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action execute;
    private readonly Func<bool>? canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();
}

/// <summary>
/// Synchronous <see cref="ICommand"/> with a typed parameter (keypad
/// digits, menu entries).
/// </summary>
/// <typeparam name="T">Parameter type.</typeparam>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> execute;
    private readonly Func<T?, bool>? canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter)
    {
        if (parameter is null)
        {
            return canExecute?.Invoke(default) ?? true;
        }

        return parameter is T typed && (canExecute?.Invoke(typed) ?? true);
    }

    public void Execute(object? parameter)
    {
        if (parameter is T typed)
        {
            execute(typed);
        }
        else if (parameter is null)
        {
            execute(default);
        }
    }
}
