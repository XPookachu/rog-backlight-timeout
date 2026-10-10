using System.Runtime.InteropServices;
using System.Text;

namespace RogBacklightTimeout.Ui;

/// <summary>
/// Gives the GUI-subsystem executable a console window for the menu (the agent never calls this, so it never flashes
/// a window) and switches it to UTF-8 with ANSI colour processing.
/// </summary>
internal static class ConsoleHost
{
    private const int StdOutputHandle = -11;
    private const uint EnableProcessedOutput = 0x0001;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    public static void Open(string title)
    {
        AllocConsole(); // fails harmlessly if a console already exists

        var output = GetStdHandle(StdOutputHandle);
        if (GetConsoleMode(output, out var mode))
            SetConsoleMode(output, mode | EnableProcessedOutput | EnableVirtualTerminalProcessing);

        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Title = title;
        try { Console.SetWindowSize(Math.Max(Console.WindowWidth, 74), Math.Max(Console.WindowHeight, 30)); }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException) { /* e.g. hosted terminal: keep its size */ }
    }
}
