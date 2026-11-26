using System.Text;

namespace RogBacklightTimeout.Diagnostics;

internal enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Where diagnostics go. Never given input content: only state changes and device paths.</summary>
internal interface ILog
{
    void Write(LogLevel level, string message);
}

internal static class LogExtensions
{
    public static void Info(this ILog log, string message) => log.Write(LogLevel.Info, message);

    public static void Warning(this ILog log, string message) => log.Write(LogLevel.Warning, message);

    public static void Error(this ILog log, string message) => log.Write(LogLevel.Error, message);
}

/// <summary>Discards everything (tests, and processes that have nowhere safe to write).</summary>
internal sealed class NullLog : ILog
{
    private NullLog()
    {
    }

    public static NullLog Instance { get; } = new();

    public void Write(LogLevel level, string message)
    {
    }
}

/// <summary>
/// Tiny size-capped file log: past <see cref="MaxBytes"/> the file becomes <c>log.txt.old</c>.
/// Several processes may append at once (the agent and an elevated install): every line is a single append with
/// shared access, so a file that is open elsewhere does not lose the line. Logging never throws.
/// </summary>
internal sealed class FileLog : ILog
{
    private const long MaxBytes = 256 * 1024;

    private readonly object _gate = new();

    public FileLog(string path) => Path = path;

    public string Path { get; }

    public void Write(LogLevel level, string message)
    {
        var line = Encoding.UTF8.GetBytes($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {Label(level)} {message}{Environment.NewLine}");
        try
        {
            lock (_gate)
            {
                var file = new FileInfo(Path);
                if (file.Exists && file.Length > MaxBytes) File.Move(Path, Path + ".old", overwrite: true);

                using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Write(line);
            }
        }
        catch (IOException)
        {
            // Logging must never take the agent down.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }

    private static string Label(LogLevel level) => level switch
    {
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        _ => "INFO ",
    };
}
