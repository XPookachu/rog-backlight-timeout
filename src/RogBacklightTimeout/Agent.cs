using System.Security.AccessControl;
using System.Security.Principal;

namespace RogBacklightTimeout;

/// <summary>The long-running per-user background process (started by Task Scheduler at logon). Also the composition root.</summary>
internal static class Agent
{
    public static int Run()
    {
        using var singleInstance = CreateMutex(out var isFirst);
        if (!isFirst)
        {
            Log.Instance.Info("Another instance is already running; exiting");
            return 0;
        }

        var log = Log.Instance;

        var platform = RogPlatform.Read();
        var backlight = new RogBacklight(platform);
        var probe = backlight.Probe();
        log.Info($"System: {platform}; backlight {probe}");
        if (!probe.Supported)
        {
            // Retryable: the controller may not be enumerated yet at logon; a non-zero exit lets the task restart us.
            log.Info("Nothing to control right now; exiting.");
            return probe.Retryable ? 3 : 0;
        }

        var settings = SettingsStore.Load();
        Settings Current() => Volatile.Read(ref settings);

        using var reload = CreateEvent(AppPaths.ReloadEventName);
        using var stop = CreateEvent(AppPaths.StopEventName);

        var levels = new GHelperLevelSource(Current, PowerStatus.IsOnBattery, log);
        using var controller = new IdleController(backlight, levels, TimeProvider.System, Current, log);

        var classifier = new InputClassifier(
            () => Current().Overrides,
            PnpInspector.Inspect,
            () => RawInputDevices.Enumerate().Where(d => d.IsKeyboard).Select(d => d.Path),
            log);
        classifier.Refresh();

        var monitor = new InputMonitor(classifier,
            (path, verdict) => log.Info($"Input device seen: {(verdict.IsInternal ? "INTERNAL" : "external")} ({verdict.Describe()}) {path}"));

        using var window = new MessageWindow();
        window.RawInput += device => { if (monitor.IsInternal(device)) controller.OnInternalInput(); };
        window.DevicesChanged += () => { classifier.Refresh(); monitor.Invalidate(); };
        window.Resumed += () => controller.OnSystemEvent("resume");
        window.PowerSourceChanged += onBattery => controller.OnSystemEvent($"power source -> {(onBattery ? "battery" : "AC")}");
        window.LidChanged += controller.OnLid;
        window.SessionDisconnected += controller.OnSessionDisconnected;

        void Shutdown(string why)
        {
            log.Info($"Stopping ({why})");
            controller.Shutdown();
            window.Quit();
        }

        // SessionEnding can still be cancelled by the user (unsaved-file prompt): only make sure the light is on and
        // keep running. SessionEnded is final.
        window.SessionEnding += controller.EnsureLit;
        window.SessionEnded += () => Shutdown("session ended");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            log.Info($"Fatal: {e.ExceptionObject}");
            try { controller.Shutdown(); } catch { /* best effort */ }
        };

        var signals = new Thread(() =>
        {
            WaitHandle[] handles = [reload, stop];
            while (true)
            {
                if (WaitHandle.WaitAny(handles) == 0)
                {
                    window.Post(() =>
                    {
                        Volatile.Write(ref settings, SettingsStore.Load());
                        classifier.Refresh();
                        monitor.Invalidate();
                        controller.OnSettingsChanged();
                        log.Info($"Config reloaded: enabled={Current().Enabled}, timeout={Current().TimeoutSeconds}s");
                    });
                }
                else
                {
                    window.Post(() => Shutdown("stop requested"));
                    return;
                }
            }
        }) { IsBackground = true, Name = "RogBacklightTimeout.Signals" };
        signals.Start();

        log.Info($"Started {AppInfo.Version}: enabled={Current().Enabled}, timeout={Current().TimeoutSeconds}s");
        controller.Start();
        window.Run();

        log.Info("Stopped");
        return 0;
    }

    // The agent runs elevated (Raw Input is blocked by UIPI while an elevated window has focus), but the menu runs at
    // normal privilege. Grant the current user access so both can talk.
    private static SecurityIdentifier CurrentUser => WindowsIdentity.GetCurrent().User!;

    private static Mutex CreateMutex(out bool createdNew)
    {
        var security = new MutexSecurity();
        security.AddAccessRule(new MutexAccessRule(CurrentUser, MutexRights.FullControl, AccessControlType.Allow));
        return MutexAcl.Create(true, AppPaths.AgentMutexName, out createdNew, security);
    }

    private static EventWaitHandle CreateEvent(string name)
    {
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(CurrentUser, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name, out _, security);
    }
}
