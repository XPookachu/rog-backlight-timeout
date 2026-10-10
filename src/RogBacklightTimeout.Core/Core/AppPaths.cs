namespace RogBacklightTimeout;

public static class AppPaths
{
    public const string TaskName = "RogBacklightTimeout";
    public const string ExeName = "RogBacklightTimeout.exe";
    public const string AgentArgument = "--agent";

    public const string AgentMutexName = @"Local\RogBacklightTimeout.Agent";
    public const string ReloadEventName = @"Local\RogBacklightTimeout.Reload";
    public const string StopEventName = @"Local\RogBacklightTimeout.Stop";

    /// <summary>Per-user data: settings and log.</summary>
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RogBacklightTimeout");

    public static string ConfigFile => Path.Combine(Dir, "config.json");
    public static string LogFile => Path.Combine(Dir, "log.txt");

    /// <summary>
    /// Machine-wide install folder. The agent runs elevated, so its executable must live somewhere only
    /// administrators can write (otherwise any normal process could replace it and be elevated at next logon).
    /// </summary>
    public static string InstallDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RogBacklightTimeout");

    public static string InstalledExe => Path.Combine(InstallDir, ExeName);
}
