using System.Security.Principal;

namespace RogBacklightTimeout.Tests;

public sealed class NativeLayoutTests
{
    /// <summary>Sizes from the Windows SDK headers for x64: a wrong layout would corrupt memory, not just fail.</summary>
    [Fact]
    public unsafe void Structures_match_the_windows_sdk_sizes()
    {
        Assert.Equal(8, IntPtr.Size); // the published exe is win-x64 only
        Assert.Equal(32, sizeof(Native.RID_DEVICE_INFO));
        Assert.Equal(24, sizeof(Native.RAWINPUTHEADER));
        Assert.Equal(16, sizeof(Native.RAWINPUTDEVICELIST));
        Assert.Equal(16, sizeof(Native.RAWINPUTDEVICE));
        Assert.Equal(80, sizeof(Native.WNDCLASSEXW));
        Assert.Equal(48, sizeof(Native.MSG));
        Assert.Equal(20, sizeof(Native.POWERBROADCAST_SETTING));
        Assert.Equal(12, sizeof(Native.SYSTEM_POWER_STATUS));
    }

    [Fact]
    public unsafe void Hid_usage_sits_where_rid_device_info_puts_it()
    {
        var info = default(Native.RID_DEVICE_INFO);
        var start = (byte*)&info;

        Assert.Equal(20, (byte*)&info.Hid.UsagePage - start);
        Assert.Equal(22, (byte*)&info.Hid.Usage - start);
    }
}

public sealed class SecureDirectoryTests
{
    [Fact]
    public void Only_administrators_and_system_are_trusted_owners()
    {
        Assert.True(SecureDirectory.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)));
        Assert.True(SecureDirectory.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));

        Assert.False(SecureDirectory.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)));
        Assert.False(SecureDirectory.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null)));
        Assert.False(SecureDirectory.IsTrustedOwner(CurrentUser.Sid)); // a folder a normal user made is not trusted
        Assert.False(SecureDirectory.IsTrustedOwner(null));
    }

    [Fact]
    public void Elevated_code_logs_outside_the_users_own_folders()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.False(AppPaths.MachineLogFile.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase));
        Assert.False(AppPaths.InstallDir.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(userProfile, AppPaths.ConfigFile, StringComparison.OrdinalIgnoreCase);
    }
}
