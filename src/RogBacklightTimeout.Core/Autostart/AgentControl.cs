namespace RogBacklightTimeout;

/// <summary>Talks to the running agent through its named mutex and events (the agent grants the current user access).</summary>
public static class AgentControl
{
    public static bool IsRunning()
    {
        var running = Mutex.TryOpenExisting(AppPaths.AgentMutexName, out var mutex);
        mutex?.Dispose();
        return running;
    }

    /// <summary>Tell the agent to re-read config.json. False when it is not running.</summary>
    public static bool Reload() => Signal(AppPaths.ReloadEventName);

    /// <summary>Ask the agent to exit (it restores the backlight first). False when it is not running.</summary>
    public static bool RequestStop() => Signal(AppPaths.StopEventName);

    /// <summary>Ask the agent to stop and wait until it has actually gone.</summary>
    public static bool StopAndWait(TimeSpan timeout)
    {
        if (!IsRunning()) return true;
        RequestStop();

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsRunning()) return true;
            Thread.Sleep(100);
        }
        return !IsRunning();
    }

    private static bool Signal(string eventName)
    {
        if (!EventWaitHandle.TryOpenExisting(eventName, out var handle)) return false;
        using (handle) handle.Set();
        return true;
    }
}
