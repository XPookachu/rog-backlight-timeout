namespace RogBacklightTimeout.Tests;

public sealed class FileLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rbt-log-{Guid.NewGuid():N}");

    public FileLogTests() => Directory.CreateDirectory(_directory);

    private string LogFile => Path.Combine(_directory, "log.txt");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Lines_carry_their_level()
    {
        var log = new FileLog(LogFile);

        log.Info("started");
        log.Warning("odd");
        log.Error("broken");

        var lines = File.ReadAllLines(LogFile);
        Assert.Equal(3, lines.Length);
        Assert.EndsWith("INFO  started", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("WARN  odd", lines[1], StringComparison.Ordinal);
        Assert.EndsWith("ERROR broken", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_log_is_moved_aside_and_a_new_one_started()
    {
        var log = new FileLog(LogFile);

        log.Info(new string('x', 300 * 1024));
        log.Info("after rotation");

        Assert.True(File.Exists(LogFile + ".old"));
        Assert.Single(File.ReadAllLines(LogFile));
    }

    [Fact]
    public void A_line_is_not_lost_while_another_process_has_the_file_open()
    {
        var log = new FileLog(LogFile);
        log.Info("first");

        using (new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            log.Info("second");

        Assert.Equal(2, File.ReadAllLines(LogFile).Length);
    }

    [Fact]
    public void Logging_never_throws_even_when_the_folder_is_gone()
    {
        var log = new FileLog(Path.Combine(_directory, "missing", "log.txt"));

        log.Error("nowhere to go");
    }
}
