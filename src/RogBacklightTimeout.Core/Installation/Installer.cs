using RogBacklightTimeout.Core;
using RogBacklightTimeout.Diagnostics;
using RogBacklightTimeout.Platform;

namespace RogBacklightTimeout.Installation;

/// <summary>
/// Self-installation: the single executable copies itself to a protected folder and registers its own logon task.
/// Both need administrator rights (the app elevates itself for them).
/// </summary>
internal sealed class Installer
{
    private const string ObsoleteSuffix = ".old";
    private const string AgentDidNotStop = "The running agent did not stop. End RogBacklightTimeout.exe in Task Manager and try again.";
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(8);

    private readonly ILog _log;

    public Installer(ILog log) => _log = log;

    /// <summary>Copy <paramref name="sourceExe"/> to the install folder, register autostart (elevated) and start the agent.</summary>
    public Outcome Install(string sourceExe)
    {
        try
        {
            if (!AgentControl.StopAndWait(StopTimeout)) return Outcome.Failure(AgentDidNotStop);

            Directory.CreateDirectory(AppPaths.InstallDir);
            DeleteObsoleteFiles();
            if (!SamePath(sourceExe, AppPaths.InstalledExe)) ReplaceInstalledExe(sourceExe);

            // The task definition is staged in the install folder (administrators only), not the user-writable temp folder.
            var task = LogonTask.Create(CurrentUser.Name, AppPaths.InstalledExe, elevated: true, AppPaths.InstallDir);
            if (!task.Ok) return Outcome.Failure($"Could not register autostart: {task.Message}");

            var run = LogonTask.Run();
            return run.Ok
                ? Outcome.Success("Installed. The agent is running and starts at logon.")
                : Outcome.Failure($"Installed, but the agent did not start: {run.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Outcome.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Stop the agent, remove autostart, the installed files and the agent's log. Settings are kept.</summary>
    public Outcome Uninstall()
    {
        try
        {
            if (!AgentControl.StopAndWait(StopTimeout)) return Outcome.Failure(AgentDidNotStop);

            if (LogonTask.Query().IsInstalled)
            {
                var deleted = LogonTask.Delete();
                if (!deleted.Ok) return Outcome.Failure($"Could not remove autostart: {deleted.Message}");
            }

            // The log folder first: removing the install folder may rename the exe this process runs from, after which
            // code that is not loaded yet can no longer be read from it.
            SecureDirectory.DeleteIfTrusted(AppPaths.MachineDataDir);
            var pending = RemoveInstallFolder();
            return Outcome.Success(pending
                ? "Autostart removed. The running copy of the program is deleted at the next restart."
                : "Autostart removed and the program deleted.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Outcome.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// A file that is still in use cannot be deleted, but it can be renamed. The renamed, uniquely named file is what
    /// gets scheduled for deletion at the next restart: scheduling the original path would also delete a copy
    /// installed there again before that restart.
    /// </summary>
    /// <returns>True when something could not be deleted now and was left for the next restart.</returns>
    private bool RemoveInstallFolder()
    {
        if (!Directory.Exists(AppPaths.InstallDir)) return false;

        var deferred = false;
        foreach (var file in Directory.EnumerateFiles(AppPaths.InstallDir, "*", SearchOption.AllDirectories).ToList())
        {
            try
            {
                Retry(() => File.Delete(file), attempts: 5);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MoveAside(file); // if even this fails the exception ends the uninstall: the file is left alone and reported
                deferred = true;
            }
        }

        try
        {
            Directory.Delete(AppPaths.InstallDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only succeeds at restart if the folder is empty by then, so a reinstalled copy is never touched.
            ScheduleDeleteAtReboot(AppPaths.InstallDir);
            deferred = true;
        }
        return deferred;
    }

    // Note: once the running exe has been renamed, assemblies that were not loaded yet can no longer be read from the
    // single-file bundle. Keep this path to types that are already in use.

    /// <summary>
    /// Installing over a copy that is still in use (a second menu window open from the installed exe): move it out of the
    /// way and write the new one.
    /// </summary>
    private void ReplaceInstalledExe(string sourceExe)
    {
        try
        {
            Retry(() => File.Copy(sourceExe, AppPaths.InstalledExe, overwrite: true), attempts: 5);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && File.Exists(AppPaths.InstalledExe))
        {
            MoveAside(AppPaths.InstalledExe);
            File.Copy(sourceExe, AppPaths.InstalledExe, overwrite: false);
        }
    }

    /// <summary>Renames an in-use file to a unique name and schedules that name (never the original path) for deletion at restart.</summary>
    private void MoveAside(string file)
    {
        var obsolete = $"{file}.{Guid.NewGuid():N}{ObsoleteSuffix}";
        File.Move(file, obsolete);
        ScheduleDeleteAtReboot(obsolete);
    }

    private void ScheduleDeleteAtReboot(string path)
    {
        if (!Native.MoveFileExW(path, null, Native.MOVEFILE_DELAY_UNTIL_REBOOT))
            _log.Warning($"Could not schedule deletion at restart: {path}");
    }

    /// <summary>Leftovers of an earlier uninstall that was still running; best effort.</summary>
    private static void DeleteObsoleteFiles()
    {
        foreach (var file in Directory.EnumerateFiles(AppPaths.InstallDir, $"*{ObsoleteSuffix}"))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still in use: the scheduled deletion at restart takes care of it.
            }
        }
    }

    /// <summary>The agent process may still be shutting down after its mutex is released (sharing violation).</summary>
    private static void Retry(Action action, int attempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < attempts)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
