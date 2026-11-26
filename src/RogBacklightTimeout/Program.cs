using RogBacklightTimeout.Core;
using RogBacklightTimeout.Diagnostics;
using RogBacklightTimeout.Ui;

namespace RogBacklightTimeout;

/// <summary>
/// One executable, three roles:
///   (no arguments)             the interactive menu, in its own console window
///   --agent                    the background agent (what the logon task runs)
///   --install / --uninstall    the elevated half of the menu's install and uninstall actions
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (Has(args, AppPaths.AgentArgument)) return Agent.Run();
        if (Has(args, Elevation.InstallArgument)) return Elevation.RunElevated(install: true, args);
        if (Has(args, Elevation.UninstallArgument)) return Elevation.RunElevated(install: false, args);
        return new Menu(AppLog.Open()).Run();
    }

    private static bool Has(string[] args, string argument) => args.Contains(argument, StringComparer.OrdinalIgnoreCase);
}
