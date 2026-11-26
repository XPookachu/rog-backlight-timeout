using System.Diagnostics;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using RogBacklightTimeout.Core;

namespace RogBacklightTimeout.Installation;

internal enum AutostartState
{
    NotInstalled,
    Installed,

    /// <summary>Registered without highest privileges: Windows hides keyboard input from it while an elevated window has focus.</summary>
    InstalledNotElevated,
}

internal sealed record AutostartInfo(AutostartState State, string? ExePath)
{
    public static AutostartInfo NotInstalled { get; } = new(AutostartState.NotInstalled, null);

    public bool IsInstalled => State != AutostartState.NotInstalled;
}

/// <summary>The Task Scheduler logon task that starts the agent (task schema 1.4, managed with schtasks.exe).</summary>
internal static class LogonTask
{
    private const string ElevatedRunLevel = "HighestAvailable";
    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static AutostartInfo Query()
    {
        var (exitCode, output) = Schtasks("/query", "/tn", AppPaths.TaskName, "/xml");
        return exitCode == 0 ? Parse(output) : AutostartInfo.NotInstalled;
    }

    /// <summary>Reads a task definition as <c>schtasks /query /xml</c> prints it.</summary>
    internal static AutostartInfo Parse(string taskXml)
    {
        try
        {
            var task = XDocument.Parse(taskXml);
            var elevated = task.Descendants(TaskNamespace + "RunLevel").Any(e => e.Value.Trim() == ElevatedRunLevel);
            var command = task.Descendants(TaskNamespace + "Command").FirstOrDefault()?.Value.Trim();
            return new AutostartInfo(elevated ? AutostartState.Installed : AutostartState.InstalledNotElevated, command);
        }
        catch (XmlException)
        {
            // The task exists but cannot be read: offer a reinstall, which replaces it.
            return new AutostartInfo(AutostartState.InstalledNotElevated, null);
        }
    }

    /// <summary>Registers (or replaces) the task. Needs administrator rights for an elevated task.</summary>
    /// <param name="user">The account whose logon starts the agent (DOMAIN\name).</param>
    /// <param name="exePath">The installed executable.</param>
    /// <param name="elevated">Run with highest privileges.</param>
    /// <param name="stagingDirectory">Where the definition is written for schtasks: a folder only administrators can write.</param>
    public static Outcome Create(string user, string exePath, bool elevated, string stagingDirectory)
    {
        var file = Path.Combine(stagingDirectory, $"RogBacklightTimeout-{Guid.NewGuid():N}.xml");
        File.WriteAllText(file, BuildXml(user, exePath, elevated), Encoding.Unicode);
        try
        {
            var (exitCode, output) = Schtasks("/create", "/tn", AppPaths.TaskName, "/xml", file, "/f");
            return new Outcome(exitCode == 0, output);
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static Outcome Delete()
    {
        var (exitCode, output) = Schtasks("/delete", "/tn", AppPaths.TaskName, "/f");
        return new Outcome(exitCode == 0, output);
    }

    /// <summary>Starts the agent through its task, so it runs with the task's privileges.</summary>
    public static Outcome Run()
    {
        var (exitCode, output) = Schtasks("/run", "/tn", AppPaths.TaskName);
        return new Outcome(exitCode == 0, output);
    }

    internal static string BuildXml(string user, string agentExePath, bool elevated) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="{TaskNamespace.NamespaceName}">
          <RegistrationInfo><Description>RogBacklightTimeout: keyboard backlight timeout for built-in input only</Description></RegistrationInfo>
          <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{Escape(user)}</UserId></LogonTrigger></Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{Escape(user)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>{(elevated ? ElevatedRunLevel : "LeastPrivilege")}</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>true</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>7</Priority>
            <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>
          </Settings>
          <Actions Context="Author"><Exec><Command>{Escape(agentExePath)}</Command><Arguments>{AppPaths.AgentArgument}</Arguments></Exec></Actions>
        </Task>
        """;

    private static string Escape(string value) => SecurityElement.Escape(value);

    private static (int ExitCode, string Output) Schtasks(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, (stdout.Result + stderr.Result).Trim());
    }
}
