namespace RogBacklightTimeout;

public interface ILog
{
    void Info(string message);

    void Error(string message) => Info("ERROR: " + message);
}

/// <summary>
/// Tiny size-capped file logger. Never logs input content: only state changes and device paths.
/// </summary>
public sealed class Log : ILog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static Log Instance { get; } = new();

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Dir);
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(file.FullName, file.FullName + ".old", overwrite: true);
                File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { /* logging must never take the agent down */ }
        catch (UnauthorizedAccessException) { }
    }
}
