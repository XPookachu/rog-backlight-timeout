using System.Runtime.InteropServices;
using System.Text;

namespace RogBacklightTimeout.Ui;

/// <summary>
/// Gives the GUI-subsystem executable a console window for the menu (the agent never calls this, so it never flashes
/// a window) and switches it to UTF-8 with ANSI colour processing.
/// </summary>
internal static partial class ConsoleHost
{
    private const int StdOutputHandle = -11;
    private const uint EnableProcessedOutput = 0x0001;
    private const uint EnableVirtualTerminalProcessing = 0x0004;
    private const int MinWidth = 74;
    private const int MinHeight = 30;

    public static void Open(string title)
    {
        AllocConsole(); // fails harmlessly if a console already exists

        var output = GetStdHandle(StdOutputHandle);
        if (GetConsoleMode(output, out var mode))
            SetConsoleMode(output, mode | EnableProcessedOutput | EnableVirtualTerminalProcessing);

        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Title = title;
        try
        {
            Console.SetWindowSize(Math.Max(Console.WindowWidth, MinWidth), Math.Max(Console.WindowHeight, MinHeight));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            // A hosted terminal (Windows Terminal) keeps its own size.
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetStdHandle(int handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(IntPtr handle, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(IntPtr handle, uint mode);
}
