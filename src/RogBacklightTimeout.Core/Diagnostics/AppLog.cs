using RogBacklightTimeout.Core;
using RogBacklightTimeout.Platform;

namespace RogBacklightTimeout.Diagnostics;

/// <summary>Picks the log for this process. A process never writes its log somewhere less trusted than itself.</summary>
internal static class AppLog
{
    /// <summary>
    /// Elevated (the agent, install, uninstall): the machine log, in a folder only administrators can write.
    /// Otherwise (the menu): the per-user log next to the settings.
    /// </summary>
    public static ILog Open()
    {
        try
        {
            if (CurrentUser.IsElevated)
            {
                SecureDirectory.Ensure(AppPaths.MachineDataDir);
                return new FileLog(AppPaths.MachineLogFile);
            }

            Directory.CreateDirectory(AppPaths.UserDataDir);
            return new FileLog(AppPaths.UserLogFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return NullLog.Instance;
        }
    }

    /// <summary>The log worth reading: the agent's if it has written one, otherwise the per-user one.</summary>
    public static string? MostRelevantFile()
    {
        if (File.Exists(AppPaths.MachineLogFile)) return AppPaths.MachineLogFile;
        return File.Exists(AppPaths.UserLogFile) ? AppPaths.UserLogFile : null;
    }
}
