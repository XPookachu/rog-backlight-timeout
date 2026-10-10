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

/// <summary>Takes snapshots. Querying Task Scheduler spawns a process, so that part is cached briefly.</summary>
internal sealed class SnapshotSource
{
    private static readonly TimeSpan AutostartCacheTime = TimeSpan.FromSeconds(6);

    private AutostartInfo? _autostart;
    private DateTime _autostartAt;

    public void InvalidateAutostart() => _autostart = null;

    public Snapshot Take()
    {
        if (_autostart is null || DateTime.UtcNow - _autostartAt > AutostartCacheTime)
        {
            _autostart = Autostart.Query();
            _autostartAt = DateTime.UtcNow;
        }

        var settings = SettingsStore.Load();
        var platform = RogPlatform.Read();
        var onBattery = PowerStatus.IsOnBattery();
        var source = new GHelperLevelSource(() => settings, () => onBattery, Log.Instance);
        var timeouts = source.ReadGHelperTimeouts();

        return new Snapshot(
            AgentRunning: AgentControl.IsRunning(),
            Autostart: _autostart,
            Settings: settings,
            Platform: platform,
            Probe: new RogBacklight(platform).Probe(),
            RestoreLevel: source.GetRestoreLevel(),
            OnBattery: onBattery,
            LevelFromGHelper: GHelperLevelSource.ResolveConfigPath(settings) is not null,
            GHelperTimeoutConflict: timeouts is { } t && (t.Battery > 0 || t.Plugged > 0));
    }
}
