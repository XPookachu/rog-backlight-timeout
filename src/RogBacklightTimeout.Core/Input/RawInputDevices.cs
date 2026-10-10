namespace RogBacklightTimeout;

/// <summary>A device known to Raw Input (type: 0 = mouse, 1 = keyboard, 2 = other HID).</summary>
internal readonly record struct RawDevice(IntPtr Handle, uint Type, string Path, ushort UsagePage, ushort Usage)
{
    public bool IsKeyboard => Type == RawInputDevices.TypeKeyboard;

    /// <summary>A precision touchpad digitizer (HID usage page 0x0D, usage 0x05).</summary>
    public bool IsTouchpad => Type == RawInputDevices.TypeHid && UsagePage == 0x0D && Usage == 0x05;

    /// <summary>The kinds of device the agent listens to.</summary>
    public bool IsRelevant => IsKeyboard || IsTouchpad;
}

internal static unsafe class RawInputDevices
{
    public const uint TypeKeyboard = 1;
    public const uint TypeHid = 2;

    private const int DeviceInfoSize = 32; // sizeof(RID_DEVICE_INFO) on 64-bit: cbSize, dwType, 24-byte union

    public static IReadOnlyList<RawDevice> Enumerate()
    {
        var size = (uint)sizeof(Native.RAWINPUTDEVICELIST);

        // Devices can come and go between the size query and the fetch (ERROR_INSUFFICIENT_BUFFER): retry.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            uint count = 0;
            if (Native.GetRawInputDeviceList(null, ref count, size) == uint.MaxValue || count == 0) return [];

            var list = new Native.RAWINPUTDEVICELIST[count];
            fixed (Native.RAWINPUTDEVICELIST* p = list)
            {
                if (Native.GetRawInputDeviceList(p, ref count, size) == uint.MaxValue) continue;
            }

            return list.Take((int)count).Select(Describe).ToArray();
        }

        return [];
    }

    public static string GetPath(IntPtr device)
    {
        uint chars = 0;
        if (Native.GetRawInputDeviceInfoW(device, Native.RIDI_DEVICENAME, null, ref chars) == uint.MaxValue || chars == 0)
            return string.Empty;

        var buffer = new char[chars];
        fixed (char* p = buffer)
        {
            var copied = Native.GetRawInputDeviceInfoW(device, Native.RIDI_DEVICENAME, p, ref chars);
            return copied == uint.MaxValue ? string.Empty : new string(p);
        }
    }

    private static RawDevice Describe(Native.RAWINPUTDEVICELIST item)
    {
        ushort usagePage = 0, usage = 0;
        if (item.Type == TypeHid)
        {
            // RID_DEVICE_INFO: cbSize(4) dwType(4) then for HID: vendorId(4) productId(4) version(4) usagePage(2) usage(2).
            var buffer = stackalloc byte[DeviceInfoSize];
            new Span<byte>(buffer, DeviceInfoSize).Clear();
            *(uint*)buffer = DeviceInfoSize;
            var size = (uint)DeviceInfoSize;
            if (Native.GetRawInputDeviceInfoW(item.Device, Native.RIDI_DEVICEINFO, buffer, ref size) != uint.MaxValue)
            {
                usagePage = *(ushort*)(buffer + 20);
                usage = *(ushort*)(buffer + 22);
            }
        }
        return new RawDevice(item.Device, item.Type, GetPath(item.Device), usagePage, usage);
    }
}
