namespace RogBacklightTimeout;

public interface IBacklight
{
    /// <summary>Sets keyboard backlight brightness (0 = off, 3 = max). Only brightness: effect/colour stay untouched.</summary>
    void SetLevel(int level);
}

public interface ILevelSource
{
    /// <summary>The brightness the user wants while active (G-Helper's current setting for AC/battery).</summary>
    int GetRestoreLevel();

    /// <summary>
    /// Changes whenever G-Helper's settings change (config file timestamp). G-Helper changes brightness on its own
    /// (Fn keys, lid, AC/battery switch), which the agent cannot see as input, so a change means "state unknown".
    /// </summary>
    long Stamp { get; }
}

/// <summary>
/// Two-state machine (Active / Dimmed) plus an "unknown" marker. Built-in input keeps the backlight on and wakes it;
/// when no built-in input arrives for the timeout, brightness is set to 0.
/// Thread-safe: input, timer, power and session events arrive on different threads.
/// </summary>
public sealed class IdleController : IDisposable
{
    private readonly IBacklight _backlight;
    private readonly ILevelSource _levels;
    private readonly TimeProvider _time;
    private readonly Func<Settings> _settings;
    private readonly ILog _log;
    private readonly object _gate = new();
    private readonly ITimer _timer;

    private long _lastActivity;
    private bool _timerArmed;
    private bool _stopped;
    private bool _dimmed;       // we set brightness 0 and believe the keyboard is dark
    private bool _maybeDark;    // G-Helper/firmware may have changed brightness behind our back: restore on first input
    private long _stampAtDim;
    private bool _lidClosed;
    private bool _disconnected;

    public IdleController(IBacklight backlight, ILevelSource levels, TimeProvider time, Func<Settings> settings, ILog log)
    {
        _backlight = backlight;
        _levels = levels;
        _time = time;
        _settings = settings;
        _log = log;
        _timer = time.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        lock (_gate) Touch();
    }

    public bool IsDimmed { get { lock (_gate) return _dimmed; } }

    /// <summary>
    /// Call once when the agent starts: if a previous instance was killed while the keyboard was dark,
    /// bring the light back to the level G-Helper wants.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            var level = SafeLevel();
            if (level > 0) TrySet(level, "startup");
        }
    }

    /// <summary>Keyboard / touchpad activity from a built-in device.</summary>
    public void OnInternalInput()
    {
        lock (_gate)
        {
            if (_stopped) return;
            Touch();
            if ((_dimmed || _maybeDark) && !_lidClosed && !_disconnected) Restore("input");
        }
    }

    /// <summary>Resume from sleep or AC/battery switch: counts as activity, never writes brightness.</summary>
    public void OnSystemEvent(string reason)
    {
        lock (_gate)
        {
            _log.Info($"System event: {reason}");
            MarkUnknown();
            Touch();
        }
    }

    public void OnLid(bool closed)
    {
        lock (_gate)
        {
            _lidClosed = closed;
            _log.Info(closed ? "Lid closed" : "Lid opened");
            if (!closed)
            {
                MarkUnknown();
                Touch();
            }
        }
    }

    /// <summary>
    /// This session lost the console (fast user switching / remote desktop disconnect): stop touching the hardware,
    /// leave the light on for whoever is at the keyboard. Plain lock/unlock needs no special handling: the lock screen
    /// delivers built-in input like the desktop does.
    /// </summary>
    public void OnSessionDisconnected(bool disconnected)
    {
        lock (_gate)
        {
            _disconnected = disconnected;
            _log.Info(disconnected ? "Session disconnected" : "Session reconnected");
            if (disconnected)
            {
                if ((_dimmed || _maybeDark) && !_lidClosed) Restore("session disconnected");
            }
            else
            {
                MarkUnknown();
                Touch();
            }
        }
    }

    /// <summary>Make sure the light is on without stopping the controller (a logoff/shutdown may still be cancelled).</summary>
    public void EnsureLit()
    {
        lock (_gate)
        {
            if ((_dimmed || _maybeDark) && !_lidClosed) Restore("session ending");
        }
    }

    /// <summary>Call after the settings object changed (enable/disable/timeout).</summary>
    public void OnSettingsChanged()
    {
        lock (_gate)
        {
            if (_stopped) return;
            if (!_settings().Enabled)
            {
                if ((_dimmed || _maybeDark) && !_lidClosed && !_disconnected) Restore("disabled");
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _timerArmed = false;
                return;
            }
            _timerArmed = false; // re-arm with the (possibly new) timeout
            Touch();
        }
    }

    /// <summary>On exit, never leave the keyboard dark.</summary>
    public void Shutdown()
    {
        lock (_gate)
        {
            _stopped = true;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timerArmed = false;
            if ((_dimmed || _maybeDark) && !_lidClosed) Restore("shutdown");
        }
    }

    public void Dispose() => _timer.Dispose();

    // ---- internals (call with _gate held) ----

    /// <summary>Something outside the agent may have changed the brightness; if we had dimmed, we no longer know the state.</summary>
    private void MarkUnknown()
    {
        if (!_dimmed) return;
        _dimmed = false;
        _maybeDark = true;
    }

    private void Touch()
    {
        _lastActivity = _time.GetTimestamp();
        if (!_timerArmed && !_stopped && _settings().Enabled) Arm(TimeSpan.FromSeconds(_settings().TimeoutSeconds));
    }

    private void Arm(TimeSpan due)
    {
        _timerArmed = true;
        _timer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private void OnTimer()
    {
        lock (_gate)
        {
            _timerArmed = false;
            if (_stopped) return;

            try
            {
                var settings = _settings();
                if (!settings.Enabled || _lidClosed || _disconnected) return;
                var timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

                if (_dimmed)
                {
                    // G-Helper changed its settings while we are dark (e.g. Fn brightness keys): the user is at the
                    // keyboard and the light may be on again. Forget "dimmed", dim again after the next idle period.
                    if (_levels.Stamp != _stampAtDim)
                    {
                        _log.Info("G-Helper settings changed while dimmed");
                        MarkUnknown();
                        _lastActivity = _time.GetTimestamp();
                    }
                    Arm(timeout); // keep watching while dimmed
                    return;
                }

                var idle = _time.GetElapsedTime(_lastActivity);
                if (idle < timeout)
                {
                    Arm(timeout - idle); // activity happened since the timer was armed
                    return;
                }

                Dim();
                // Keep looking: if nothing was dimmed (backlight off in G-Helper right now, or the write failed) a later
                // change is picked up; if we dimmed, watch for G-Helper changing brightness behind our back.
                Arm(timeout);
            }
            catch (Exception ex)
            {
                // A timer callback must never take the agent down.
                _log.Info($"Timer error: {ex.Message}");
                if (!_stopped && _settings().Enabled) Arm(TimeSpan.FromSeconds(_settings().TimeoutSeconds));
            }
        }
    }

    private void Dim()
    {
        var level = SafeLevel();
        if (level <= 0)
        {
            _maybeDark = false; // already dark by user choice: nothing to do, nothing to write
            return;
        }

        if (TrySet(0, "idle timeout"))
        {
            _dimmed = true;
            _maybeDark = false;
            _stampAtDim = _levels.Stamp;
        }
    }

    private void Restore(string reason)
    {
        var level = SafeLevel();
        if (level > 0 && !TrySet(level, reason)) return; // keep state so the next input retries
        _dimmed = false;
        _maybeDark = false;
    }

    private int SafeLevel()
    {
        try { return _levels.GetRestoreLevel(); }
        catch (Exception ex)
        {
            _log.Info($"Could not determine brightness level: {ex.Message}");
            return _settings().FallbackLevel;
        }
    }

    private bool TrySet(int level, string reason)
    {
        try
        {
            _backlight.SetLevel(level);
            _log.Info($"Backlight -> {level} ({reason})");
            return true;
        }
        catch (Exception ex)
        {
            _log.Info($"Backlight write failed ({reason}): {ex.Message}");
            return false;
        }
    }
}
