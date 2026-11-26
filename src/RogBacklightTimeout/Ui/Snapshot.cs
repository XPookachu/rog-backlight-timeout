using RogBacklightTimeout.Core;
using RogBacklightTimeout.Diagnostics;
using RogBacklightTimeout.Installation;
using RogBacklightTimeout.Integrations;
using RogBacklightTimeout.Platform;
using RogBacklightTimeout.Rog;

namespace RogBacklightTimeout.Ui;

/// <summary>Everything the menu shows, gathered in one place.</summary>
internal sealed record Snapshot(
    bool AgentRunning,
    AutostartInfo Autostart,
    Settings Settings,
    PlatformInfo Platform,
    ProbeResult Probe,
    int RestoreLevel,
    bool OnBattery,
    bool LevelFromGHelper,
    bool GHelperTimeoutConflict);

/// <summary>
/// Takes the menu's snapshots (about once a second). Only cheap, changing facts are read every time: the laptop model
/// never changes, the controller check opens HID devices and is repeated only while the controller is missing, and
/// querying Task Scheduler starts a process, so that is cached briefly.
/// </summary>
internal sealed class SnapshotSource
{
    private static readonly TimeSpan AutostartCacheTime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ReprobeInterval = TimeSpan.FromSeconds(10);

    private readonly ILog _log;
    private readonly PlatformInfo _platform = RogPlatform.Read();
    private readonly GHelperLevelSource _levels;
    private Settings _settings = new();
    private bool _onBattery;
    private Timed<ProbeResult>? _probe;
    private Timed<AutostartInfo>? _autostart;

    public SnapshotSource(ILog log)
    {
        _log = log;
        _levels = new GHelperLevelSource(() => _settings, () => _onBattery, log);
    }

    /// <summary>Re-read the install state (and the controller) on the next snapshot, e.g. after install or uninstall.</summary>
    public void Invalidate()
    {
        _autostart = null;
        _probe = null;
    }

    public Snapshot Take()
    {
        if (_autostart is null || _autostart.Age > AutostartCacheTime) _autostart = new(LogonTask.Query());

        // The controller can appear late at logon: look again while it is missing, otherwise once.
        if (_probe is null || (!_probe.Value.Supported && _probe.Value.Retryable && _probe.Age > ReprobeInterval))
            _probe = new(new RogBacklight(_platform).Probe());

        _settings = SettingsStore.Load(_log);
        _onBattery = PowerStatus.IsOnBattery();
        var timeouts = _levels.ReadGHelperTimeouts();

        return new Snapshot(
            AgentRunning: AgentControl.IsRunning(),
            Autostart: _autostart.Value,
            Settings: _settings,
            Platform: _platform,
            Probe: _probe.Value,
            RestoreLevel: _levels.GetRestoreLevel(),
            OnBattery: _onBattery,
            LevelFromGHelper: _levels.ConfigPath is not null,
            GHelperTimeoutConflict: timeouts is { } t && (t.Battery > 0 || t.Plugged > 0));
    }

    /// <summary>A value and when it was read.</summary>
    private sealed class Timed<T>
    {
        private readonly long _readAt = Environment.TickCount64;

        public Timed(T value) => Value = value;

        public T Value { get; }

        public TimeSpan Age => TimeSpan.FromMilliseconds(Environment.TickCount64 - _readAt);
    }
}
