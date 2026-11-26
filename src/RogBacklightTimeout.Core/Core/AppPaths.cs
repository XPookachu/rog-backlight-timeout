namespace RogBacklightTimeout.Core;

/// <summary>Names and locations shared by the menu, the installer and the agent.</summary>
internal static class AppPaths
{
    public const string TaskName = "RogBacklightTimeout";
    public const string ExeName = "RogBacklightTimeout.exe";
    public const string AgentArgument = "--agent";

    public const string AgentMutexName = @"Local\RogBacklightTimeout.Agent";
    public const string ReloadEventName = @"Local\RogBacklightTimeout.Reload";
    public const string StopEventName = @"Local\RogBacklightTimeout.Stop";

    private const string FolderName = "RogBacklightTimeout";
    private const string LogFileName = "log.txt";

    /// <summary>Per-user data: the settings, and the log of anything that runs without administrator rights.</summary>
    public static string UserDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    public static string ConfigFile => Path.Combine(UserDataDir, "config.json");

    public static string UserLogFile => Path.Combine(UserDataDir, LogFileName);

    /// <summary>
    /// Machine-wide data: the log of elevated code (the agent, install and uninstall). Elevated code never writes into
    /// the user's own folders, because any normal process could replace those with links and redirect the write.
    /// The folder is created only administrators can write (<see cref="Platform.SecureDirectory"/>).
    /// </summary>
    public static string MachineDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), FolderName);

    public static string MachineLogFile => Path.Combine(MachineDataDir, LogFileName);

    /// <summary>
    /// Machine-wide install folder. The agent runs elevated, so its executable must live somewhere only
    /// administrators can write (otherwise any normal process could replace it and be elevated at next logon).
    /// </summary>
    public static string InstallDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), FolderName);

    public static string InstalledExe => Path.Combine(InstallDir, ExeName);
}
