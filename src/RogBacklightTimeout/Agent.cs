using System.Security.AccessControl;
using RogBacklightTimeout.Core;
using RogBacklightTimeout.Diagnostics;
using RogBacklightTimeout.Input;
using RogBacklightTimeout.Integrations;
using RogBacklightTimeout.Platform;
using RogBacklightTimeout.Rog;

namespace RogBacklightTimeout;

/// <summary>The long-running per-user background process (started by Task Scheduler at logon). Also the composition root.</summary>
internal sealed class Agent
{
    /// <summary>Exit code when no controller appeared in time (it may still appear later, e.g. after a driver update).</summary>
    private const int ExitControllerMissing = 3;
    private const int ProbeRetries = 24;
    private static readonly TimeSpan ProbeRetryInterval = TimeSpan.FromSeconds(5);

    private readonly ILog _log;
    private Settings _settings = new();

    private Agent(ILog log) => _log = log;

    public static int Run()
    {
        var log = AppLog.Open();
        using var singleInstance = CreateMutex(out var isFirst);
        if (!isFirst)
        {
            log.Info("Another instance is already running; exiting");
            return 0;
        }

        return new Agent(log).Execute();
    }

    /// <summary>Read from every thread; replaced as a whole on reload.</summary>
    private Settings CurrentSettings() => Volatile.Read(ref _settings);

    private int Execute()
    {
        var platform = RogPlatform.Read();
        var backlight = new RogBacklight(platform);
        using var reload = CreateEvent(AppPaths.ReloadEventName);
        using var stop = CreateEvent(AppPaths.StopEventName);

        var probe = backlight.Probe();
        _log.Info($"System: {platform}; backlight {probe}");

        // The controller may not be enumerated yet right after logon. Task Scheduler does not restart a task that
        // merely exits with a non-zero code, so retry here for a while before giving up.
        for (var attempt = 1; !probe.Supported && probe.Retryable && attempt <= ProbeRetries; attempt++)
        {
            if (stop.WaitOne(ProbeRetryInterval)) return 0;
            probe = backlight.Probe();
            if (probe.Supported) _log.Info($"Backlight controller found after {attempt} retr{(attempt == 1 ? "y" : "ies")}: {probe}");
        }

        if (!probe.Supported)
        {
            _log.Warning("Nothing to control right now; exiting.");
            return probe.Retryable ? ExitControllerMissing : 0;
        }

        Volatile.Write(ref _settings, SettingsStore.Load(_log));

        var levels = new GHelperLevelSource(CurrentSettings, PowerStatus.IsOnBattery, _log);
        using var controller = new IdleController(backlight, levels, TimeProvider.System, CurrentSettings, _log);

        var classifier = new InputClassifier(
            () => CurrentSettings().Overrides,
            PnpInspector.Inspect,
            () => RawInputDevices.Enumerate().Where(d => d.IsKeyboard).Select(d => d.Path),
            _log);
        classifier.Refresh();

        var monitor = new InputMonitor(classifier,
            (path, verdict) => _log.Info($"Input device seen: {(verdict.IsInternal ? "INTERNAL" : "external")} ({verdict.Describe()}) {path}"));

        using var window = new MessageWindow(_log);
        window.RawInput += device =>
        {
            if (monitor.IsInternal(device)) controller.OnInternalInput();
        };

        // Fn-row keys arrive on other collections of the built-in keyboard (see InputSources): listen to those too.
        // Runs once the window exists, and again whenever devices or the overrides change.
        void ListenToKeyChannels()
        {
            try
            {
                var added = window.ListenTo(InputSources.Select(RawInputDevices.Enumerate(), path => classifier.Classify(path).IsInternal));
                if (added.Count > 0) _log.Info($"Listening to input collections: {string.Join(", ", added.Select(u => $"{u.Page:X4}/{u.Id:X2}"))}");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                _log.Warning($"Could not listen to the keyboard's extra collections (Fn keys will not wake the backlight): {ex.Message}");
            }
        }

        void Reload()
        {
            Volatile.Write(ref _settings, SettingsStore.Load(_log));
            classifier.Refresh();
            monitor.Invalidate();
            ListenToKeyChannels();
            controller.OnSettingsChanged();
            _log.Info($"Config reloaded: enabled={CurrentSettings().Enabled}, timeout={CurrentSettings().TimeoutSeconds}s");
        }

        void Shutdown(string why)
        {
            _log.Info($"Stopping ({why})");
            controller.Shutdown();
            window.Quit();
        }

        window.Post(ListenToKeyChannels);
        window.DevicesChanged += () =>
        {
            classifier.Refresh();
            monitor.Invalidate();
            ListenToKeyChannels();
        };
        window.Resumed += () => controller.OnSystemEvent("resume");
        window.PowerSourceChanged += onBattery => controller.OnSystemEvent($"power source -> {(onBattery ? "battery" : "AC")}");
        window.LidChanged += controller.OnLid;
        window.SessionDisconnected += controller.OnSessionDisconnected;

        // SessionEnding can still be cancelled by the user (unsaved-file prompt): only make sure the light is on and
        // keep running. SessionEnded is final.
        window.SessionEnding += controller.EnsureLit;
        window.SessionEnded += () => Shutdown("session ended");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            _log.Error($"Fatal: {e.ExceptionObject}");
#pragma warning disable CA1031 // Best effort while the process is going down: nothing may stop the light being restored.
            try
            {
                controller.Shutdown();
            }
            catch
            {
                // Nothing left to do.
            }
#pragma warning restore CA1031
        };

        StartSignalThread(reload, stop, () => window.Post(Reload), () => window.Post(() => Shutdown("stop requested")));

        _log.Info($"Started {AppInfo.Version}: enabled={CurrentSettings().Enabled}, timeout={CurrentSettings().TimeoutSeconds}s");
        controller.Start();
        window.Run();

        _log.Info("Stopped");
        return 0;
    }

    /// <summary>Waits for the menu's reload and stop events and hands them to the message loop.</summary>
    private static void StartSignalThread(EventWaitHandle reload, EventWaitHandle stop, Action onReload, Action onStop)
    {
        var thread = new Thread(() =>
        {
            WaitHandle[] handles = [reload, stop];
            while (WaitHandle.WaitAny(handles) == 0) onReload();
            onStop();
        })
        {
            IsBackground = true,
            Name = "RogBacklightTimeout.Signals",
        };
        thread.Start();
    }

    // The agent runs elevated (Raw Input is blocked by UIPI while an elevated window has focus), but the menu runs at
    // normal privilege. Grant the current user access so both can talk.
    private static Mutex CreateMutex(out bool createdNew)
    {
        var security = new MutexSecurity();
        security.AddAccessRule(new MutexAccessRule(CurrentUser.Sid, MutexRights.FullControl, AccessControlType.Allow));
        return MutexAcl.Create(true, AppPaths.AgentMutexName, out createdNew, security);
    }

    private static EventWaitHandle CreateEvent(string name)
    {
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(CurrentUser.Sid, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name, out _, security);
    }
}
