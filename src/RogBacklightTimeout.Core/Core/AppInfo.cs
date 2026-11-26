using System.Reflection;

namespace RogBacklightTimeout.Core;

/// <summary>Build information of the running program.</summary>
internal static class AppInfo
{
    /// <summary>The release version, taken from the git tag at build time (MinVer), without build metadata.</summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "dev";
}
