using System.Security.AccessControl;
using System.Security.Principal;

namespace RogBacklightTimeout.Platform;

/// <summary>
/// A folder for files that elevated code writes: owned by Administrators, writable only by Administrators and SYSTEM,
/// readable by everyone.
///
/// %ProgramData% lets any user create a sub-folder and own it, so an existing folder is trusted only when it is a real
/// folder owned by Administrators or SYSTEM. Anything else (a link, or a folder someone else created) is renamed out of
/// the way, never deleted or written into: renaming a link moves the link itself, so nothing it points to is touched.
/// </summary>
internal static class SecureDirectory
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    /// <summary>Makes sure <paramref name="path"/> is a trusted folder. Needs administrator rights.</summary>
    public static void Ensure(string path)
    {
        var directory = new DirectoryInfo(path);
        if (directory.Exists && IsTrusted(directory)) return;

        if (directory.Exists)
        {
            directory.MoveTo($"{path}.untrusted-{Guid.NewGuid():N}");
            directory = new DirectoryInfo(path);
        }

        // Throws if someone creates the folder again in between: such a folder is never trusted by accident.
        directory.Create(CreateSecurity());
    }

    /// <summary>Deletes the folder and its contents, but only when it is trusted (so never through a link).</summary>
    public static void DeleteIfTrusted(string path)
    {
        var directory = new DirectoryInfo(path);
        if (directory.Exists && IsTrusted(directory)) directory.Delete(recursive: true);
    }

    internal static bool IsTrustedOwner(SecurityIdentifier? owner) =>
        owner is not null && (owner == Administrators || owner == LocalSystem);

    private static bool IsTrusted(DirectoryInfo directory) =>
        !directory.Attributes.HasFlag(FileAttributes.ReparsePoint)
        && IsTrustedOwner(directory.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier);

    private static DirectorySecurity CreateSecurity()
    {
        const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var security = new DirectorySecurity();
        security.SetOwner(Administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, Inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }
}
