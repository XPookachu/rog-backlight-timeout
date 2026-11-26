using System.ComponentModel;
using System.Diagnostics;
using RogBacklightTimeout.Core;
using RogBacklightTimeout.Diagnostics;
using RogBacklightTimeout.Installation;
using RogBacklightTimeout.Platform;

namespace RogBacklightTimeout;

/// <summary>
/// Install and uninstall need administrator rights. The menu runs as the user, so it re-launches this exe elevated
/// for that one action and waits for it; the elevated process reports success through its exit code.
/// </summary>
internal static class Elevation
{
    public const string InstallArgument = "--install";
    public const string UninstallArgument = "--uninstall";
    private const string UserArgument = "--user";
    private const int ErrorCancelled = 1223;

    /// <summary>Called by the menu: performs the action, asking for administrator approval when needed.</summary>
    public static Outcome Request(bool install, ILog log)
    {
        // Already an administrator (e.g. started from an elevated terminal): no second UAC prompt.
        if (CurrentUser.IsElevated) return Perform(install, log);

        try
        {
            var argument = install ? InstallArgument : UninstallArgument;
            using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{argument} {UserArgument} {CurrentUser.Sid}")
            {
                UseShellExecute = true,
                Verb = "runas",
            })!;
            child.WaitForExit();
            return child.ExitCode == 0
                ? Outcome.Success(install ? "Installed. The agent is running and starts at logon." : "Uninstalled.")
                : Outcome.Failure($"{Verb(install)} failed. Details are in {AppPaths.MachineLogFile}.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return Outcome.Failure("Cancelled.");
        }
    }

    /// <summary>The elevated process: do the work, log the outcome, report success through the exit code.</summary>
    public static int RunElevated(bool install, string[] args)
    {
        var log = AppLog.Open();

        // UAC may have been approved with another administrator's credentials: that account would own the task.
        var index = Array.FindIndex(args, a => string.Equals(a, UserArgument, StringComparison.OrdinalIgnoreCase));
        var expected = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        if (expected is not null && !string.Equals(expected, CurrentUser.Sid.Value, StringComparison.OrdinalIgnoreCase))
        {
            log.Error($"{Verb(install)}: FAILED - elevated as a different user; run it from the account that will use it, and approve with that account.");
            return 1;
        }

        return Perform(install, log).Ok ? 0 : 1;
    }

    private static Outcome Perform(bool install, ILog log)
    {
        var installer = new Installer(log);
        var outcome = install ? installer.Install(Environment.ProcessPath!) : installer.Uninstall();
        log.Write(outcome.Ok ? LogLevel.Info : LogLevel.Error, $"{Verb(install)}: {(outcome.Ok ? "ok" : "FAILED")} - {outcome.Message}");
        return outcome;
    }

    private static string Verb(bool install) => install ? "Install" : "Uninstall";
}
