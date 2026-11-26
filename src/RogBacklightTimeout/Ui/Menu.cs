using System.Diagnostics;
using System.Text;
using RogBacklightTimeout.Core;
using RogBacklightTimeout.Diagnostics;
using RogBacklightTimeout.Installation;
using RogBacklightTimeout.Rog;

namespace RogBacklightTimeout.Ui;

internal enum Tone
{
    Info,
    Good,
    Bad,
}

internal sealed record Notice(Tone Tone, string Text);

/// <summary>The interactive menu: a status panel and a short list of actions. Arrow keys or number keys, Enter to select.</summary>
internal sealed class Menu
{
    private const string Title = "ROG Backlight Timeout";
    private const int Width = 64;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan KeyPollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(6);
    private static readonly string Rule = "  " + Ansi.Gray + new string('─', Width - 2) + Ansi.Reset;

    private readonly ILog _log;
    private readonly SnapshotSource _source;
    private bool _quit;

    public Menu(ILog log)
    {
        _log = log;
        _source = new SnapshotSource(log);
    }

    /// <summary>One menu entry. <c>Run</c> may show progress through the callback and returns the notice to show afterwards.</summary>
    private sealed record Item(string Label, Func<Action<Notice>, Notice?> Run);

    public int Run()
    {
        ConsoleHost.Open(Title);
        Console.Write(Ansi.ClearScreen + Ansi.HideCursor);

        try
        {
            var snapshot = _source.Take();
            var items = BuildItems(snapshot);
            var nextRefresh = Stopwatch.StartNew();
            var selected = 0;
            Notice? notice = null;
            var lastFrame = "";

            void Draw()
            {
                var frame = Render(snapshot, items, selected, notice);
                if (frame == lastFrame) return;
                Console.Write(Ansi.Home + frame + Ansi.ClearBelow);
                lastFrame = frame;
            }

            void Refresh()
            {
                snapshot = _source.Take();
                items = BuildItems(snapshot);
                selected = Math.Clamp(selected, 0, items.Count - 1);
                nextRefresh.Restart();
            }

            while (!_quit)
            {
                if (nextRefresh.Elapsed >= RefreshInterval) Refresh();
                Draw();

                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(KeyPollInterval);
                    continue;
                }

                var pick = HandleKey(Console.ReadKey(intercept: true), items.Count, ref selected);
                if (pick < 0) continue;

                selected = pick;
                notice = null;
                notice = items[pick].Run(shown =>
                {
                    notice = shown;
                    Draw();
                });
                if (_quit) break; // do not touch anything else: the exe this process runs from may be gone
                Console.Write(Ansi.ClearScreen); // actions may have used the whole screen
                lastFrame = "";
                Refresh();
            }
        }
        finally
        {
            Console.Write(Ansi.ShowCursor + Ansi.Reset);
        }
        return 0;
    }

    /// <summary>Moves the selection; returns the index of the item to run, or -1.</summary>
    private int HandleKey(ConsoleKeyInfo key, int count, ref int selected)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                selected = (selected + count - 1) % count;
                return -1;
            case ConsoleKey.DownArrow:
                selected = (selected + 1) % count;
                return -1;
            case ConsoleKey.Home:
                selected = 0;
                return -1;
            case ConsoleKey.End:
                selected = count - 1;
                return -1;
            case ConsoleKey.Enter:
                return selected;
            case ConsoleKey.Escape:
            case ConsoleKey.Q:
                _quit = true;
                return -1;
            default:
                return key.KeyChar is >= '1' and <= '9' && key.KeyChar - '1' < count ? key.KeyChar - '1' : -1;
        }
    }

    // ---- items ----

    private List<Item> BuildItems(Snapshot s)
    {
        var items = new List<Item>
        {
            new(s.Settings.Enabled ? "Turn timeout off" : "Turn timeout on", _ => ToggleTimeout()),
            new("Set timeout…", _ => SetTimeout(s.Settings.TimeoutSeconds)),
        };

        if (s.Autostart.IsInstalled)
        {
            items.Add(s.AgentRunning
                ? new Item("Stop agent", _ => StopAgent())
                : new Item("Start agent", _ => StartAgent()));
        }

        items.Add(new Item(s.Autostart.IsInstalled ? "Reinstall / update" : "Install (start at logon)", show => Install(s, show)));

        if (s.Autostart.IsInstalled || Directory.Exists(AppPaths.InstallDir))
            items.Add(new Item("Uninstall", Uninstall));

        items.Add(new Item("Input devices", _ =>
        {
            Screens.Devices(s.Settings, _log);
            return null;
        }));
        items.Add(new Item("Open log", _ => OpenLog()));
        items.Add(new Item("Quit", _ =>
        {
            _quit = true;
            return null;
        }));
        return items;
    }

    private Notice ToggleTimeout()
    {
        var settings = SettingsStore.Load(_log);
        var enabled = !settings.Enabled;
        SettingsStore.Save(settings with { Enabled = enabled });
        return Applied($"Timeout turned {(enabled ? "on" : "off")}.");
    }

    private Notice? SetTimeout(int current)
    {
        var seconds = Screens.AskSeconds(current);
        if (seconds is null) return null;

        SettingsStore.Save(SettingsStore.Load(_log) with { TimeoutSeconds = seconds.Value });
        return Applied($"Timeout set to {seconds.Value} seconds.");
    }

    /// <summary>Tell a running agent to pick up the new settings.</summary>
    private static Notice Applied(string text) => AgentControl.Reload()
        ? new Notice(Tone.Good, text)
        : new Notice(Tone.Good, text + " It applies when the agent starts.");

    private static Notice StartAgent()
    {
        var run = LogonTask.Run();
        return run.Ok ? new Notice(Tone.Good, "Agent started.") : new Notice(Tone.Bad, $"Could not start the agent: {run.Message}");
    }

    private static Notice StopAgent() =>
        AgentControl.StopAndWait(StopTimeout)
            ? new Notice(Tone.Good, "Agent stopped. The backlight is back on.")
            : new Notice(Tone.Bad, "The agent did not stop. End RogBacklightTimeout.exe in Task Manager.");

    private Notice Install(Snapshot s, Action<Notice> show)
    {
        if (!s.Probe.Supported && !s.Probe.Retryable)
            return new Notice(Tone.Bad, $"This laptop is not supported: {s.Probe.Detail}.");

        show(new Notice(Tone.Info, "Waiting for administrator approval…"));
        var outcome = Elevation.Request(install: true, _log);
        _source.Invalidate();
        return ToNotice(outcome);
    }

    private Notice? Uninstall(Action<Notice> show)
    {
        if (!Screens.Confirm("Uninstall ROG Backlight Timeout?",
                "This stops the agent, removes autostart and deletes the installed program and its log.",
                $"Your settings in {AppPaths.UserDataDir} are kept."))
            return null;

        show(new Notice(Tone.Info, "Waiting for administrator approval…"));
        var outcome = Elevation.Request(install: false, _log);
        _source.Invalidate();

        // This window runs from the file that was just removed: assemblies not loaded yet could no longer be read from it,
        // so say goodbye and close instead of carrying on.
        if (outcome.Ok && RunsFromInstallFolder())
        {
            show(new Notice(Tone.Good, $"{outcome.Message} Press any key to close."));
            Console.ReadKey(intercept: true);
            _quit = true;
        }
        return ToNotice(outcome);
    }

    private static Notice ToNotice(Outcome outcome) => new(outcome.Ok ? Tone.Good : Tone.Bad, outcome.Message);

    private static bool RunsFromInstallFolder()
    {
        var exe = Environment.ProcessPath;
        return exe is not null
            && Path.GetFullPath(exe).StartsWith(Path.GetFullPath(AppPaths.InstallDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static Notice OpenLog()
    {
        var file = AppLog.MostRelevantFile();
        if (file is null) return new Notice(Tone.Info, "There is no log yet.");

        var startInfo = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
        startInfo.ArgumentList.Add(file);
        using var notepad = Process.Start(startInfo);
        return new Notice(Tone.Info, $"Opened {file} in Notepad.");
    }

    // ---- rendering ----

    private static string Render(Snapshot s, IReadOnlyList<Item> items, int selected, Notice? notice)
    {
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append(Ansi.ClearToEndOfLine).Append("\r\n");

        var version = $"v{AppInfo.Version}";
        Line();
        Line($"  {Ansi.Bold}{Ansi.Cyan}{Title}{Ansi.Reset}{new string(' ', Math.Max(1, Width - 2 - Title.Length - version.Length))}{Ansi.Gray}{version}{Ansi.Reset}");
        Line($"  {Ansi.Gray}Keyboard backlight timeout for ROG laptops · built-in input only{Ansi.Reset}");
        Line();
        Line(Rule);

        Line(Row("Agent", s.AgentRunning ? Tone.Good : Tone.Bad, s.AgentRunning ? "Running" : "Not running"));
        Line(Row("Autostart", AutostartTone(s.Autostart), AutostartText(s.Autostart)));
        Line(Row("Timeout", s.Settings.Enabled ? Tone.Good : Tone.Bad,
            s.Settings.Enabled ? $"On · {s.Settings.TimeoutSeconds} seconds" : "Off"));
        Line(BrightnessRow(s));
        Line(Row("Laptop", s.Probe.Supported ? Tone.Good : Tone.Bad,
            s.Probe.Supported ? $"{ShortModel(s.Platform)} · supported" : $"{ShortModel(s.Platform)} · {s.Probe.Detail}"));
        if (s.GHelperTimeoutConflict)
            Line(Row("G-Helper", Tone.Bad, "Its own keyboard timeout is on and will fight this one. Set it to Off."));

        Line(Rule);
        for (var i = 0; i < items.Count; i++)
        {
            var marker = i == selected ? Ansi.Cyan + Ansi.Bold + "›" : " ";
            var label = i == selected ? Ansi.Bold + Ansi.Cyan + items[i].Label : items[i].Label;
            Line($"  {marker} {Ansi.Gray}{i + 1}{Ansi.Reset}  {label}{Ansi.Reset}");
        }
        Line(Rule);
        Line($"  {Ansi.Gray}↑↓ move · Enter select · 1-{items.Count} pick · Q quit{Ansi.Reset}");
        Line();
        Line(notice is null ? "" : "  " + NoticeText(notice));
        return sb.ToString();
    }

    private static string Row(string label, Tone tone, string text)
    {
        var (color, dot) = tone switch
        {
            Tone.Good => (Ansi.Green, "●"),
            Tone.Bad => (Ansi.Yellow, "●"),
            _ => (Ansi.Gray, "○"),
        };
        return $"  {Ansi.Gray}{label,-11}{Ansi.Reset} {color}{dot}{Ansi.Reset} {text}";
    }

    private static string BrightnessRow(Snapshot s)
    {
        var power = s.OnBattery ? "on battery" : "plugged in";
        if (s.RestoreLevel == 0)
            return Row("Brightness", Tone.Bad, $"Off in G-Helper ({power}), so there is nothing to dim");

        var from = s.LevelFromGHelper ? "from G-Helper" : "default (G-Helper config not found)";
        return Row("Brightness", Tone.Good, $"Level {s.RestoreLevel} · {power} · {Ansi.Gray}{from}{Ansi.Reset}");
    }

    private static Tone AutostartTone(AutostartInfo info) => info.State switch
    {
        AutostartState.Installed => Tone.Good,
        AutostartState.InstalledNotElevated => Tone.Bad,
        _ => Tone.Info,
    };

    private static string AutostartText(AutostartInfo info) => info.State switch
    {
        AutostartState.Installed => "Installed · starts at logon",
        AutostartState.InstalledNotElevated => "Installed without admin rights · reinstall to fix",
        _ => "Not installed",
    };

    private static string NoticeText(Notice notice) => notice.Tone switch
    {
        Tone.Good => $"{Ansi.Green}✔{Ansi.Reset} {notice.Text}",
        Tone.Bad => $"{Ansi.Red}✖{Ansi.Reset} {notice.Text}",
        _ => $"{Ansi.Cyan}…{Ansi.Reset} {notice.Text}",
    };

    private static string ShortModel(PlatformInfo platform) =>
        string.IsNullOrWhiteSpace(platform.Product) ? "Unknown laptop" : platform.Product;
}
