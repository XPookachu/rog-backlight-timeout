using System.Security.Principal;

namespace RogBacklightTimeout;

/// <summary>
/// Self-installation: the single executable copies itself to a protected folder and registers its own logon task.
/// Both operations need administrator rights (the app elevates itself for them).
/// </summary>
public static class Installer
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(8);
    private const string ObsoleteSuffix = ".old";

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
            DeleteObsoleteFiles();
            if (!SamePath(sourceExe, AppPaths.InstalledExe))
                Retry(() => File.Copy(sourceExe, AppPaths.InstalledExe, overwrite: true));

            using var identity = WindowsIdentity.GetCurrent();
            // The task definition is written into the install folder (administrators only), not the user-writable temp folder.
            var task = Autostart.Create(identity.Name, AppPaths.InstalledExe, elevated: true, AppPaths.InstallDir);
            if (!task.Ok) return (false, $"Could not register autostart: {task.Message}");

            var run = Autostart.Run();
            return run.Ok
                ? (true, "Installed. The agent is running and starts at logon.")
                : (false, $"Installed, but the agent did not start: {run.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
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
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// A file that is still in use cannot be deleted, but it can be renamed. The renamed, uniquely named file is what
    /// gets scheduled for deletion at the next restart: scheduling the original path would also delete a copy
    /// installed there again before that restart.
    /// </summary>
    /// <returns>True when something could not be deleted now and was left for the next restart.</returns>
    private static bool RemoveInstallFolder()
    {
        if (!Directory.Exists(AppPaths.InstallDir)) return false;

        var deferred = false;
        foreach (var file in Directory.EnumerateFiles(AppPaths.InstallDir, "*", SearchOption.AllDirectories).ToList())
        {
            try { Retry(() => File.Delete(file), attempts: 5); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var obsolete = $"{file}.{Guid.NewGuid():N}{ObsoleteSuffix}";
                try
                {
                    File.Move(file, obsolete);
                    ScheduleDeleteAtReboot(obsolete);
                }
                catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
                {
                    ScheduleDeleteAtReboot(file); // cannot rename either: nothing safer left
                }
                deferred = true;
            }
        }

        try { Directory.Delete(AppPaths.InstallDir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only succeeds at restart if the folder is empty by then, so a reinstalled copy is never touched.
            ScheduleDeleteAtReboot(AppPaths.InstallDir);
            deferred = true;
        }
        return deferred;
    }

    // Note: once the running exe has been renamed, assemblies that were not loaded yet can no longer be read from the
    // single-file bundle. Keep this path to types that are already in use (no Marshal.GetLastWin32Error here).
    private static void ScheduleDeleteAtReboot(string path)
    {
        if (!Native.MoveFileExW(path, null, Native.MOVEFILE_DELAY_UNTIL_REBOOT))
            Log.Instance.Info($"Could not schedule deletion at restart: {path}");
    }

    /// <summary>Leftovers of an earlier uninstall that was still running; best effort.</summary>
    private static void DeleteObsoleteFiles()
    {
        foreach (var file in Directory.EnumerateFiles(AppPaths.InstallDir, $"*{ObsoleteSuffix}"))
        {
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>The agent process may still be shutting down after its mutex is released (sharing violation).</summary>
    private static void Retry(Action action, int attempts = 10)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { action(); return; }
            catch (IOException) when (attempt < attempts) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) when (attempt < attempts) { Thread.Sleep(200); }
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
