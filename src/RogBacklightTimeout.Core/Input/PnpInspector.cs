using System.Runtime.InteropServices;

namespace RogBacklightTimeout;

/// <summary>
/// Reads Plug and Play facts for a Raw Input device path using CfgMgr32.
/// Property keys are the documented DEVPKEY_* values from devpkey.h in the Windows SDK:
/// https://learn.microsoft.com/windows-hardware/drivers/install/devpkey-device-containerid
/// https://learn.microsoft.com/windows-hardware/drivers/install/devpkey-device-removalpolicy
/// </summary>
internal static unsafe class PnpInspector
{
    private const uint CrSuccess = 0;
    private const uint DevPropTypeInt32 = 0x00000006;
    private const uint DevPropTypeUInt32 = 0x00000007;
    private const uint DevPropTypeGuid = 0x0000000D;
    private const uint DevPropTypeString = 0x00000012;
    private const int MaxAncestors = 16;
    private const int BufferBytes = 1024;

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid Fmtid;
        public uint Pid;
    }

    private static readonly DevPropKey InstanceIdKey = new() { Fmtid = new("78c34fc8-104a-4aca-9ea4-524d52996e57"), Pid = 256 };
    private static readonly DevPropKey ContainerIdKey = new() { Fmtid = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), Pid = 2 };
    // Keys verified against Windows itself (Get-PnpDeviceProperty reports {A45C254E-...} 33 for DEVPKEY_Device_RemovalPolicy).
    private static readonly DevPropKey RemovalPolicyKey = new() { Fmtid = new("a45c254e-df1c-4efd-8020-67d146a850e0"), Pid = 33 };

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_PropertyW(string interfacePath, in DevPropKey key, out uint type, byte* buffer, ref uint size, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_PropertyW(uint devInst, in DevPropKey key, out uint type, byte* buffer, ref uint size, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_IDW(uint devInst, char* buffer, uint bufferLength, uint flags);

    /// <summary>Returns null only if the interface path cannot be resolved to a device at all.</summary>
    public static DeviceFacts? Inspect(string interfacePath)
    {
        if (string.IsNullOrEmpty(interfacePath)) return null;

        var instanceId = ReadInterfaceString(interfacePath, InstanceIdKey);
        if (instanceId is null) return null;

        if (CM_Locate_DevNodeW(out var node, instanceId, 0) != CrSuccess)
            return new DeviceFacts(interfacePath, instanceId, null, null, []);

        var container = ReadNode<Guid>(node, ContainerIdKey, DevPropTypeGuid);
        var policy = ReadNode<uint>(node, RemovalPolicyKey, DevPropTypeUInt32, DevPropTypeInt32);

        var ancestors = new List<string>();
        var current = node;
        for (var i = 0; i < MaxAncestors; i++)
        {
            if (CM_Get_Parent(out var parent, current, 0) != CrSuccess) break;
            var id = ReadDeviceId(parent);
            if (id is null) break;
            ancestors.Add(id);
            current = parent;
        }

        return new DeviceFacts(interfacePath, instanceId, container, policy, ancestors);
    }

    private static string? ReadInterfaceString(string interfacePath, in DevPropKey key)
    {
        var buffer = stackalloc byte[BufferBytes];
        var size = (uint)BufferBytes;
        if (CM_Get_Device_Interface_PropertyW(interfacePath, in key, out var type, buffer, ref size, 0) != CrSuccess) return null;
        return type == DevPropTypeString ? new string((char*)buffer) : null;
    }

    private static T? ReadNode<T>(uint node, in DevPropKey key, uint expectedType, uint alternateType = 0) where T : unmanaged
    {
        var buffer = stackalloc byte[BufferBytes];
        var size = (uint)BufferBytes;
        if (CM_Get_DevNode_PropertyW(node, in key, out var type, buffer, ref size, 0) != CrSuccess) return null;
        if ((type != expectedType && type != alternateType) || size != (uint)sizeof(T)) return null;
        return *(T*)buffer;
    }

    private static string? ReadDeviceId(uint node)
    {
        var buffer = stackalloc char[BufferBytes / sizeof(char)];
        return CM_Get_Device_IDW(node, buffer, BufferBytes / sizeof(char), 0) == CrSuccess ? new string(buffer) : null;
    }
}
