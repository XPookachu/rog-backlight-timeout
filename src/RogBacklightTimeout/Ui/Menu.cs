using System.Diagnostics;
using System.Text;

namespace RogBacklightTimeout.Ui;

internal enum Tone { Info, Good, Bad }

internal sealed record Notice(Tone Tone, string Text);

/// <summary>The interactive menu: a status panel and a short list of actions. Arrow keys or number keys, Enter to select.</summary>
internal static class Menu
{
    private const string Title = "ROG Backlight Timeout";
    private const int Width = 64;
    private static readonly string Rule = "  " + Ansi.Gray + new string('─', Width - 2) + Ansi.Reset;

    private sealed record Item(string Label, Func<Action<Notice>, Notice?> Run);

    private static bool _quit;

    public static int Run()
    {
        ConsoleHost.Open(Title);
        var source = new SnapshotSource();
        Console.Write(Ansi.ClearScreen + Ansi.HideCursor);

        try
        {
            Snapshot snapshot = source.Take();
            List<Item> items = BuildItems(snapshot, source);
            var nextPoll = DateTime.UtcNow.AddSeconds(1);
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
                snapshot = source.Take();
                items = BuildItems(snapshot, source);
                selected = Math.Clamp(selected, 0, items.Count - 1);
                nextPoll = DateTime.UtcNow.AddSeconds(1);
            }

            while (!_quit)
            {
                if (DateTime.UtcNow >= nextPoll) Refresh();
                Draw();

                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(50);
                    continue;
                }

                var key = Console.ReadKey(intercept: true);
                var pick = -1;
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow: selected = (selected + items.Count - 1) % items.Count; break;
                    case ConsoleKey.DownArrow: selected = (selected + 1) % items.Count; break;
                    case ConsoleKey.Home: selected = 0; break;
                    case ConsoleKey.End: selected = items.Count - 1; break;
                    case ConsoleKey.Enter: pick = selected; break;
                    case ConsoleKey.Escape:
                    case ConsoleKey.Q: _quit = true; break;
                    default:
                        if (key.KeyChar is >= '1' and <= '9' && key.KeyChar - '1' < items.Count) pick = key.KeyChar - '1';
                        break;
                }

                if (pick < 0) continue;
                selected = pick;
                notice = null;
                notice = items[pick].Run(shown => { notice = shown; Draw(); });
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

    // ---- items ----

    private static List<Item> BuildItems(Snapshot s, SnapshotSource source)
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

        items.Add(new Item(
            s.Autostart.IsInstalled ? "Reinstall / update" : "Install (start at logon)",
            show => Install(s, show, source)));

        if (s.Autostart.IsInstalled || Directory.Exists(AppPaths.InstallDir))
            items.Add(new Item("Uninstall", show => Uninstall(show, source)));

        items.Add(new Item("Input devices", _ => { Screens.Devices(s.Settings); return null; }));
        items.Add(new Item("Open log", _ => OpenLog()));
        items.Add(new Item("Quit", _ => { _quit = true; return null; }));
        return items;
    }

    private static Notice ToggleTimeout()
    {
        var settings = SettingsStore.Load();
        var enabled = !settings.Enabled;
        SettingsStore.Save(settings with { Enabled = enabled });
        return Applied($"Timeout turned {(enabled ? "on" : "off")}.");
    }

    private static Notice? SetTimeout(int current)
    {
        var seconds = Screens.AskSeconds(current);
        if (seconds is null) return null;

        SettingsStore.Save(SettingsStore.Load() with { TimeoutSeconds = seconds.Value });
        return Applied($"Timeout set to {seconds.Value} seconds.");
    }

    /// <summary>Tell a running agent to pick up the new settings.</summary>
    private static Notice Applied(string text) => AgentControl.Reload()
        ? new Notice(Tone.Good, text)
        : new Notice(Tone.Good, text + " It applies when the agent starts.");

    private static Notice StartAgent()
    {
        var (ok, message) = Autostart.Run();
        return ok ? new Notice(Tone.Good, "Agent started.") : new Notice(Tone.Bad, $"Could not start the agent: {message}");
    }

    private static Notice StopAgent() =>
        AgentControl.StopAndWait(TimeSpan.FromSeconds(6))
            ? new Notice(Tone.Good, "Agent stopped. The backlight is back on.")
            : new Notice(Tone.Bad, "The agent did not stop. End RogBacklightTimeout.exe in Task Manager.");

    private static Notice Install(Snapshot s, Action<Notice> show, SnapshotSource source)
    {
        if (!s.Probe.Supported && !s.Probe.Retryable)
            return new Notice(Tone.Bad, $"This laptop is not supported: {s.Probe.Detail}.");

        show(new Notice(Tone.Info, "Waiting for administrator approval…"));
        var (ok, message) = Program.Elevated.Request(install: true);
        source.InvalidateAutostart();
        return new Notice(ok ? Tone.Good : Tone.Bad, message);
    }

    private static Notice? Uninstall(Action<Notice> show, SnapshotSource source)
    {
        if (!Screens.Confirm("Uninstall ROG Backlight Timeout?",
                "This stops the agent, removes autostart and deletes the installed program.",
                $"Your settings and log in {AppPaths.Dir} are kept."))
            return null;

        show(new Notice(Tone.Info, "Waiting for administrator approval…"));
        var (ok, message) = Program.Elevated.Request(install: false);
        source.InvalidateAutostart();

        // This window runs from the file that was just removed: assemblies not loaded yet could no longer be read from it,
        // so say goodbye and close instead of carrying on.
        if (ok && RunsFromInstallFolder())
        {
            show(new Notice(Tone.Good, $"{message} Press any key to close."));
            Console.ReadKey(intercept: true);
            _quit = true;
        }
        return new Notice(ok ? Tone.Good : Tone.Bad, message);
    }

    private static bool RunsFromInstallFolder()
    {
        var exe = Environment.ProcessPath;
        return exe is not null
            && Path.GetFullPath(exe).StartsWith(Path.GetFullPath(AppPaths.InstallDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static Notice OpenLog()
    {
        if (!File.Exists(AppPaths.LogFile)) return new Notice(Tone.Info, "There is no log yet.");
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppPaths.LogFile}\"") { UseShellExecute = false });
        return new Notice(Tone.Info, "Opened the log in Notepad.");
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
