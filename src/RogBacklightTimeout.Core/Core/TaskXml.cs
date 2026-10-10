using System.Security;

namespace RogBacklightTimeout;

/// <summary>Builds the Task Scheduler definition for the logon autostart (schema 1.4).</summary>
public static class TaskXml
{
    public static string Build(string user, string agentExePath, bool elevated) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Description>RogBacklightTimeout: keyboard backlight timeout for built-in input only</Description></RegistrationInfo>
          <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{Escape(user)}</UserId></LogonTrigger></Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{Escape(user)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>{(elevated ? "HighestAvailable" : "LeastPrivilege")}</RunLevel>
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
}
