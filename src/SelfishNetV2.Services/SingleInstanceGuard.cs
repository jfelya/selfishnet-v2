namespace SelfishNetV2.Services;

/// <summary>
/// One named mutex per user session. Two SelfishNet instances must never shape the
/// same LAN: each would poison and heal independently. Released before an elevated
/// relaunch so the new process can take over.
/// </summary>
public static class SingleInstanceGuard
{
    public const string DefaultName = "SelfishNetV2.SingleInstance";

    private static Mutex? _mutex;
    private static string? _name;

    public static bool TryAcquire(string name)
    {
        if (_mutex != null) return false;
        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died; the mutex is now ours.
        }
        _mutex = mutex;
        _name = name;
        return true;
    }

    /// <summary>Try to take the same name again after a cancelled UAC relaunch.</summary>
    public static bool Reacquire() => _name != null && TryAcquire(_name);

    public static void Release()
    {
        var mutex = _mutex;
        _mutex = null;
        if (mutex == null) return;
        try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        mutex.Dispose();
    }
}
