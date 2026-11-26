using RogBacklightTimeout.Platform;

namespace RogBacklightTimeout.Input;

/// <summary>A device known to Raw Input (type: 0 = mouse, 1 = keyboard, 2 = other HID).</summary>
internal readonly record struct RawDevice(IntPtr Handle, uint Type, string Path, ushort UsagePage, ushort Usage)
{
    public bool IsKeyboard => Type == RawInputDevices.TypeKeyboard;

    /// <summary>A precision touchpad digitizer (HID usage page 0x0D, usage 0x05).</summary>
    public bool IsTouchpad => Type == RawInputDevices.TypeHid && UsagePage == HidUsage.PrecisionTouchpad.Page && Usage == HidUsage.PrecisionTouchpad.Id;

    /// <summary>The kinds of device the agent listens to.</summary>
    public bool IsRelevant => IsKeyboard || IsTouchpad;
}

internal static unsafe class RawInputDevices
{
    public const uint TypeKeyboard = Native.RIM_TYPEKEYBOARD;
    public const uint TypeHid = Native.RIM_TYPEHID;

    private const int EnumerationAttempts = 3;

    public static IReadOnlyList<RawDevice> Enumerate()
    {
        var size = (uint)sizeof(Native.RAWINPUTDEVICELIST);

        // Devices can come and go between the size query and the fetch (ERROR_INSUFFICIENT_BUFFER): retry.
        for (var attempt = 0; attempt < EnumerationAttempts; attempt++)
        {
            uint count = 0;
            if (Native.GetRawInputDeviceList(null, ref count, size) == Native.RawInputError || count == 0) return [];

            var list = new Native.RAWINPUTDEVICELIST[count];
            fixed (Native.RAWINPUTDEVICELIST* p = list)
            {
                if (Native.GetRawInputDeviceList(p, ref count, size) == Native.RawInputError) continue;
            }

            return list.Take((int)count).Select(Describe).ToArray();
        }

        return [];
    }

    public static string GetPath(IntPtr device)
    {
        uint chars = 0;
        if (Native.GetRawInputDeviceInfoW(device, Native.RIDI_DEVICENAME, null, ref chars) == Native.RawInputError || chars == 0)
            return string.Empty;

        var buffer = new char[chars];
        fixed (char* p = buffer)
        {
            var copied = Native.GetRawInputDeviceInfoW(device, Native.RIDI_DEVICENAME, p, ref chars);
            return copied == Native.RawInputError ? string.Empty : new string(p);
        }
    }

    private static RawDevice Describe(Native.RAWINPUTDEVICELIST item)
    {
        ushort usagePage = 0, usage = 0;
        if (item.Type == TypeHid)
        {
            var info = new Native.RID_DEVICE_INFO { cbSize = (uint)sizeof(Native.RID_DEVICE_INFO) };
            var size = info.cbSize;
            if (Native.GetRawInputDeviceInfoW(item.Device, Native.RIDI_DEVICEINFO, &info, ref size) != Native.RawInputError)
            {
                usagePage = info.Hid.UsagePage;
                usage = info.Hid.Usage;
            }
        }
        return new RawDevice(item.Device, item.Type, GetPath(item.Device), usagePage, usage);
    }
}
