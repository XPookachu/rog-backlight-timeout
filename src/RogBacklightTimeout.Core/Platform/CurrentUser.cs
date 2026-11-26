using System.Security.Principal;

namespace RogBacklightTimeout.Platform;

/// <summary>The Windows account this process runs as.</summary>
internal static class CurrentUser
{
    public static SecurityIdentifier Sid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User!;
        }
    }

    /// <summary>DOMAIN\name, as Task Scheduler expects it.</summary>
    public static string Name
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
    }

    /// <summary>True when running with administrator rights (elevated).</summary>
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
