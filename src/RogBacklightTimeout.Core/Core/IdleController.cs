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
/// Keeps the keyboard backlight dark while no built-in input arrives for the timeout, and lit otherwise.
/// Brightness is write-only for us, so the controller tracks what it believes the hardware is doing
/// (<see cref="KeyboardState"/>). Every event updates the activity timestamp or a flag, then <see cref="Reconcile"/>
/// is the single place that decides whether to dim or restore.
/// Thread-safe: input, timer, power and session events arrive on different threads.
/// </summary>
public sealed class IdleController : IDisposable
{
    private enum KeyboardState
    {
        /// <summary>Light is on (or deliberately off in G-Helper): nothing to restore.</summary>
        Lit,

        /// <summary>We set brightness 0 and believe the keyboard is dark.</summary>
        Dark,

        /// <summary>G-Helper/firmware may have changed brightness behind our back after we dimmed: restore on first input.</summary>
        Unknown,
    }

    private readonly IBacklight _backlight;
    private readonly ILevelSource _levels;
    private readonly TimeProvider _time;
    private readonly Func<Settings> _settings;
    private readonly ILog _log;
    private readonly object _gate = new();
    private readonly ITimer _timer;

    private KeyboardState _state = KeyboardState.Lit;
    private long _lastActivity;
    private long _stampAtDim;
    private bool _timerArmed;
    private bool _stopped;
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

    public bool IsDimmed { get { lock (_gate) return _state == KeyboardState.Dark; } }

    /// <summary>
    /// Call once when the agent starts. A previous instance may have been killed while the keyboard was dark, so the
    /// state is unknown: nothing is written now (the lid state is not known yet, and a failed write could not be
    /// retried); the first built-in input restores the light, retrying until the write succeeds.
    /// </summary>
    public void Start()
    {
        lock (_gate) _state = KeyboardState.Unknown;
    }

    /// <summary>Keyboard / touchpad activity from a built-in device.</summary>
    public void OnInternalInput()
    {
        lock (_gate)
        {
            if (_stopped) return;
            Touch();
            Reconcile("input", userPresent: true);
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
                Reconcile("session disconnected");
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
        lock (_gate) RestoreIfNeeded("session ending");
    }

    /// <summary>Call after the settings object changed (enable/disable/timeout).</summary>
    public void OnSettingsChanged()
    {
        lock (_gate)
        {
            if (_stopped) return;
            if (!_settings().Enabled)
            {
                Reconcile("disabled");
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
            RestoreIfNeeded("shutdown");
        }
    }

    public void Dispose() => _timer.Dispose();

    // ---- internals (call with _gate held) ----

    /// <summary>
    /// The one decision point. Never writes while the lid is closed.
    /// Restore when the light must be on: the user is present, the feature is off, or this session has no console.
    /// Otherwise dim once the idle period has passed.
    /// </summary>
    private void Reconcile(string reason, bool userPresent = false)
    {
        var settings = _settings();
        if (userPresent || _disconnected || !settings.Enabled)
        {
            RestoreIfNeeded(reason);
            return;
        }

        if (_lidClosed || _state == KeyboardState.Dark) return;
        if (_time.GetElapsedTime(_lastActivity) >= TimeSpan.FromSeconds(settings.TimeoutSeconds)) Dim();
    }

    /// <summary>Something outside the agent may have changed the brightness; if we had dimmed, we no longer know the state.</summary>
    private void MarkUnknown()
    {
        if (_state == KeyboardState.Dark) _state = KeyboardState.Unknown;
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

                // G-Helper changed its settings while we are dark (e.g. Fn brightness keys): the user is at the
                // keyboard and the light may be on again. Forget "dimmed", dim again after the next idle period.
                if (_state == KeyboardState.Dark && _levels.Stamp != _stampAtDim)
                {
                    _log.Info("G-Helper settings changed while dimmed");
                    MarkUnknown();
                    _lastActivity = _time.GetTimestamp();
                }

                Reconcile("idle timeout");

                // Keep looking: if nothing was dimmed (backlight off in G-Helper right now, or the write failed) a later
                // change is picked up; if we dimmed, watch for G-Helper changing brightness behind our back.
                var idle = _time.GetElapsedTime(_lastActivity);
                Arm(idle < timeout ? timeout - idle : timeout);
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
            _state = KeyboardState.Lit; // already dark by user choice: nothing to do, nothing to write
            return;
        }

        if (TrySet(0, "idle timeout"))
        {
            _state = KeyboardState.Dark;
            _stampAtDim = _levels.Stamp;
        }
    }

    private void RestoreIfNeeded(string reason)
    {
        if (_lidClosed || _state == KeyboardState.Lit) return;

        var level = SafeLevel();
        if (level > 0 && !TrySet(level, reason)) return; // keep state so the next input retries
        _state = KeyboardState.Lit;
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
