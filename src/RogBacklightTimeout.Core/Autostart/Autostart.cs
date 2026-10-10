using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace RogBacklightTimeout;

public enum AutostartState
{
    NotInstalled,
    Installed,
    /// <summary>Registered without highest privileges: Windows hides keyboard input from it while an elevated window has focus.</summary>
    InstalledNotElevated,
}

public sealed record AutostartInfo(AutostartState State, string? ExePath)
{
    public bool IsInstalled => State != AutostartState.NotInstalled;
}

/// <summary>The logon task that starts the agent (Task Scheduler, via schtasks.exe).</summary>
public static class Autostart
{
    public static AutostartInfo Query()
    {
        var (code, output) = Schtasks($"/query /tn {AppPaths.TaskName} /xml");
        if (code != 0) return new AutostartInfo(AutostartState.NotInstalled, null);

        var elevated = output.Contains("HighestAvailable", StringComparison.OrdinalIgnoreCase);
        var command = Regex.Match(output, @"<Command>(?<c>.*?)</Command>", RegexOptions.Singleline);
        var exe = command.Success ? System.Net.WebUtility.HtmlDecode(command.Groups["c"].Value.Trim()) : null;
        return new AutostartInfo(elevated ? AutostartState.Installed : AutostartState.InstalledNotElevated, exe);
    }

    /// <summary>Needs an administrator for the elevated task.</summary>
    /// <param name="xmlDirectory">Where the task definition is staged for schtasks. Use a folder only administrators can write.</param>
    public static (bool Ok, string Message) Create(string user, string exePath, bool elevated, string? xmlDirectory = null)
    {
        var file = Path.Combine(xmlDirectory ?? Path.GetTempPath(), $"RogBacklightTimeout-{Guid.NewGuid():N}.xml");
        File.WriteAllText(file, TaskXml.Build(user, exePath, elevated), Encoding.Unicode);
        try
        {
            var (code, output) = Schtasks($"/create /tn {AppPaths.TaskName} /xml \"{file}\" /f");
            return (code == 0, output);
        }
        finally { File.Delete(file); }
    }

    public static (bool Ok, string Message) Delete()
    {
        var (code, output) = Schtasks($"/delete /tn {AppPaths.TaskName} /f");
        return (code == 0, output);
    }

    /// <summary>Starts the agent through its task (so it runs with the task's privileges).</summary>
    public static (bool Ok, string Message) Run()
    {
        var (code, output) = Schtasks($"/run /tn {AppPaths.TaskName}");
        return (code == 0, output);
    }

    private static (int ExitCode, string Output) Schtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, (stdout.Result + stderr.Result).Trim());
    }
}
