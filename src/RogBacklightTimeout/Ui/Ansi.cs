namespace RogBacklightTimeout.Ui;

/// <summary>ANSI escape sequences (supported by Windows Terminal and the modern console host).</summary>
internal static class Ansi
{
    public const string Reset = "\x1b[0m";
    public const string Bold = "\x1b[1m";
    public const string Dim = "\x1b[2m";
    public const string Red = "\x1b[31m";
    public const string Green = "\x1b[32m";
    public const string Yellow = "\x1b[33m";
    public const string Cyan = "\x1b[36m";
    public const string Gray = "\x1b[90m";

    public const string ClearScreen = "\x1b[2J\x1b[H";
    public const string Home = "\x1b[H";
    public const string ClearToEndOfLine = "\x1b[K";
    public const string ClearBelow = "\x1b[J";
    public const string HideCursor = "\x1b[?25l";
    public const string ShowCursor = "\x1b[?25h";

    public static string Paint(string color, string text) => color + text + Reset;
}
