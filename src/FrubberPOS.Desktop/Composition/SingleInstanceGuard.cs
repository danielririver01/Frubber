namespace FrubberPOS.Desktop.Composition;

/// <summary>
/// Single-instance gate (P-DD-004, JHO-18). Holds a session-local named
/// mutex from startup to exit. A second process detects the held mutex,
/// shows a notice and terminates without touching data.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\FrubberPOS.SingleInstance.v1";

    private readonly Mutex mutex;
    private bool disposed;

    private SingleInstanceGuard(Mutex mutex)
    {
        this.mutex = mutex;
    }

    public static bool TryAcquire(out SingleInstanceGuard? guard)
    {
        guard = null;
        var mutex = new Mutex(initiallyOwned: false, name: MutexName, createdNew: out _);

        bool acquired;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // Previous process died without releasing. The mutex is ours now;
            // continue normally instead of blocking the user.
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            return false;
        }

        guard = new SingleInstanceGuard(mutex);
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned (should not happen); releasing is best-effort.
        }

        mutex.Dispose();
    }
}
