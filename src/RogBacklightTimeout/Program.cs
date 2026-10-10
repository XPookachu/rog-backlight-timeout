namespace RogBacklightTimeout;

/// <summary>
/// One executable, three roles:
///   (no arguments)  interactive menu in its own console window
///   --agent         the background agent (what the logon task runs)
///   --install / --uninstall  the elevated half of the menu's install and uninstall actions
/// </summary>
internal static class Program
{
    private const string InstallArgument = "--install";
    private const string UninstallArgument = "--uninstall";
    private const string UserArgument = "--user";

    public static int Main(string[] args)
    {
        if (args.Contains(AppPaths.AgentArgument, StringComparer.OrdinalIgnoreCase)) return Agent.Run();
        if (args.Contains(InstallArgument, StringComparer.OrdinalIgnoreCase)) return Elevated.Run(install: true, args);
        if (args.Contains(UninstallArgument, StringComparer.OrdinalIgnoreCase)) return Elevated.Run(install: false, args);
        return Ui.Menu.Run();
    }

    /// <summary>Re-launches this exe with administrator rights for one action and waits for it.</summary>
    internal static class Elevated
    {
        public static (bool Ok, string Message) Request(bool install)
        {
            var userSid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            // Already an administrator (e.g. started from an elevated terminal): no second UAC prompt.
            if (Installer.IsElevated())
                return install ? Installer.Install(Environment.ProcessPath!) : Installer.Uninstall();

            try
            {
                using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    Environment.ProcessPath!, $"{(install ? InstallArgument : UninstallArgument)} {UserArgument} {userSid}")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                })!;
                child.WaitForExit();
                return child.ExitCode == 0
                    ? (true, install ? "Installed. The agent is running and starts at logon." : "Uninstalled.")
                    : (false, $"{(install ? "Install" : "Uninstall")} failed. Details are in the log.");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
            {
                return (false, "Cancelled.");
            }
        }

        /// <summary>The elevated process: do the work, log the outcome, report success through the exit code.</summary>
        public static int Run(bool install, string[] args)
        {
            // UAC may have been approved with another administrator's credentials: that account would own the task.
            var index = Array.FindIndex(args, a => string.Equals(a, UserArgument, StringComparison.OrdinalIgnoreCase));
            var expected = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            var actual = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            if (expected is not null && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                Log.Instance.Info($"{(install ? "Install" : "Uninstall")}: FAILED - elevated as a different user; run it from the account that will use it, and approve with that account.");
                return 1;
            }

            var (ok, message) = install ? Installer.Install(Environment.ProcessPath!) : Installer.Uninstall();
            Log.Instance.Info($"{(install ? "Install" : "Uninstall")}: {(ok ? "ok" : "FAILED")} - {message}");
            return ok ? 0 : 1;
        }
    }
}
