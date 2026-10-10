namespace RogBacklightTimeout.Ui;

/// <summary>Full-screen prompts and the device list. Each one takes over the screen; the menu redraws afterwards.</summary>
internal static class Screens
{
    /// <summary>Asks for the idle timeout. Returns null when cancelled.</summary>
    public static int? AskSeconds(int current)
    {
        var input = current.ToString();
        string? error = null;
        Console.Write(Ansi.ClearScreen + Ansi.ShowCursor);

        try
        {
            while (true)
            {
                Console.Write(Ansi.Home);
                Console.Write($"\r\n  {Ansi.Bold}{Ansi.Cyan}Set timeout{Ansi.Reset}{Ansi.ClearToEndOfLine}\r\n");
                Console.Write($"  {Ansi.Gray}Seconds without built-in keyboard or touchpad input before the backlight turns off.{Ansi.Reset}{Ansi.ClearToEndOfLine}\r\n\r\n");
                Console.Write($"  Seconds ({Settings.MinTimeoutSeconds}-{Settings.MaxTimeoutSeconds}): {input}{Ansi.ClearToEndOfLine}\r\n\r\n");
                Console.Write($"  {Ansi.Gray}Enter to save · Esc to cancel{Ansi.Reset}{Ansi.ClearToEndOfLine}\r\n");
                Console.Write(error is null ? Ansi.ClearToEndOfLine : $"\r\n  {Ansi.Red}✖{Ansi.Reset} {error}{Ansi.ClearToEndOfLine}");
                Console.Write(Ansi.ClearBelow);

                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                        return null;
                    case ConsoleKey.Enter:
                        if (int.TryParse(input, out var seconds) && seconds is >= Settings.MinTimeoutSeconds and <= Settings.MaxTimeoutSeconds)
                            return seconds;
                        error = $"Enter a whole number from {Settings.MinTimeoutSeconds} to {Settings.MaxTimeoutSeconds}.";
                        break;
                    case ConsoleKey.Backspace:
                        if (input.Length > 0) input = input[..^1];
                        error = null;
                        break;
                    default:
                        if (char.IsAsciiDigit(key.KeyChar) && input.Length < 4) input += key.KeyChar;
                        error = null;
                        break;
                }
            }
        }
        finally { Console.Write(Ansi.HideCursor); }
    }

    /// <summary>Yes/no question, default no.</summary>
    public static bool Confirm(string title, params string[] lines)
    {
        Console.Write(Ansi.ClearScreen);
        Console.Write($"\r\n  {Ansi.Bold}{Ansi.Cyan}{title}{Ansi.Reset}\r\n\r\n");
        foreach (var line in lines) Console.Write($"  {line}\r\n");
        Console.Write($"\r\n  {Ansi.Gray}Press{Ansi.Reset} Y {Ansi.Gray}to continue, any other key to cancel.{Ansi.Reset}\r\n");
        return Console.ReadKey(intercept: true).Key == ConsoleKey.Y;
    }

    /// <summary>The keyboards and touchpads that can wake the backlight, and whether each counts as built-in.</summary>
    public static void Devices(Settings settings)
    {
        Console.Write(Ansi.ClearScreen);
        Console.Write($"\r\n  {Ansi.Bold}{Ansi.Cyan}Input devices{Ansi.Reset}\r\n");
        Console.Write($"  {Ansi.Gray}Only built-in devices keep the backlight on or wake it.{Ansi.Reset}\r\n\r\n");

        var devices = RawInputDevices.Enumerate().Where(d => d.IsRelevant).ToList();
        var classifier = new InputClassifier(
            () => settings.Overrides, PnpInspector.Inspect,
            () => devices.Where(d => d.IsKeyboard).Select(d => d.Path),
            Log.Instance);
        classifier.Refresh();

        foreach (var device in devices.OrderByDescending(d => classifier.Classify(d.Path).IsInternal).ThenBy(d => d.IsTouchpad))
        {
            var verdict = classifier.Classify(device.Path);
            var kind = device.IsTouchpad ? "Touchpad" : "Keyboard";
            var (color, text) = verdict.IsInternal ? (Ansi.Green, "Built-in") : (Ansi.Gray, "External");
            Console.Write($"  {color}●{Ansi.Reset} {color}{text,-9}{Ansi.Reset} {kind,-9} {ShortId(device.Path)}\r\n");
            Console.Write($"      {Ansi.Gray}{verdict.Describe()}{Ansi.Reset}\r\n");
        }

        if (devices.Count == 0)
            Console.Write($"  {Ansi.Yellow}No keyboards or touchpads were found.{Ansi.Reset}\r\n");
        else if (!classifier.HasBuiltInKeyboard)
            Console.Write($"\r\n  {Ansi.Yellow}No keyboard was recognised as built-in, so all of them count.{Ansi.Reset}\r\n" +
                          $"  {Ansi.Gray}If that is wrong, add part of its ID to internalDeviceOverrides in {AppPaths.ConfigFile}{Ansi.Reset}\r\n");

        Console.Write($"\r\n  {Ansi.Gray}Press any key to go back.{Ansi.Reset}\r\n");
        Console.ReadKey(intercept: true);
    }

    /// <summary>\\?\HID#VID_0B05&amp;PID_19B6&amp;MI_00&amp;Col02#8&amp;...#{guid} -> HID\VID_0B05&amp;PID_19B6&amp;MI_00&amp;COL02</summary>
    private static string ShortId(string interfacePath)
    {
        var parts = interfacePath.TrimStart('\\', '?').Split('#');
        return parts.Length >= 2 ? $"{parts[0]}\\{parts[1].ToUpperInvariant()}" : interfacePath;
    }
}
