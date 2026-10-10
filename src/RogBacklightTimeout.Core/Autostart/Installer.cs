using System.Security.Principal;

namespace RogBacklightTimeout;

/// <summary>
/// Self-installation: the single executable copies itself to a protected folder and registers its own logon task.
/// Both operations need administrator rights (the app elevates itself for them).
/// </summary>
public static class Installer
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(8);

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Copy <paramref name="sourceExe"/> to the install folder, register autostart (elevated) and start the agent.</summary>
    public static (bool Ok, string Message) Install(string sourceExe)
    {
        try
        {
            if (!AgentControl.StopAndWait(StopTimeout))
                return (false, "The running agent did not stop. End RogBacklightTimeout.exe in Task Manager and try again.");

            Directory.CreateDirectory(AppPaths.InstallDir);
            if (!SamePath(sourceExe, AppPaths.InstalledExe))
                File.Copy(sourceExe, AppPaths.InstalledExe, overwrite: true);

            using var identity = WindowsIdentity.GetCurrent();
            var task = Autostart.Create(identity.Name, AppPaths.InstalledExe, elevated: true);
            if (!task.Ok) return (false, $"Could not register autostart: {task.Message}");

            var run = Autostart.Run();
            return run.Ok
                ? (true, "Installed. The agent is running and starts at logon.")
                : (false, $"Installed, but the agent did not start: {run.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Stop the agent, remove autostart and the installed files. Settings and the log are kept.</summary>
    public static (bool Ok, string Message) Uninstall()
    {
        try
        {
            if (!AgentControl.StopAndWait(StopTimeout))
                return (false, "The running agent did not stop. End RogBacklightTimeout.exe in Task Manager and try again.");

            if (Autostart.Query().IsInstalled)
            {
                var deleted = Autostart.Delete();
                if (!deleted.Ok) return (false, $"Could not remove autostart: {deleted.Message}");
            }

            var pending = RemoveInstallFolder();
            return (true, pending
                ? "Autostart removed. The running copy of the program is deleted at the next restart."
                : "Autostart removed and the program deleted.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, ex.Message);
        }
    }

    /// <returns>True when something could not be deleted now and was scheduled for the next restart.</returns>
    private static bool RemoveInstallFolder()
    {
        if (!Directory.Exists(AppPaths.InstallDir)) return false;

        var deferred = false;
        foreach (var file in Directory.EnumerateFiles(AppPaths.InstallDir, "*", SearchOption.AllDirectories))
        {
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use (this very process): Windows deletes it on the next restart.
                Native.MoveFileExW(file, null, Native.MOVEFILE_DELAY_UNTIL_REBOOT);
                deferred = true;
            }
        }

        try { Directory.Delete(AppPaths.InstallDir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Native.MoveFileExW(AppPaths.InstallDir, null, Native.MOVEFILE_DELAY_UNTIL_REBOOT);
            deferred = true;
        }
        return deferred;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
