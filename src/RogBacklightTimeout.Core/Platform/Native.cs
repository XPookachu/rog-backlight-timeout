using System.Runtime.InteropServices;

namespace RogBacklightTimeout.Platform;

/// <summary>
/// The Win32 surface the agent uses (source-generated P/Invoke). Names follow the Windows SDK headers so they can be
/// looked up directly; everything else in the program is managed code.
/// </summary>
internal static unsafe partial class Native
{
    // Window messages
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_QUERYENDSESSION = 0x0011;
    public const uint WM_ENDSESSION = 0x0016;
    public const uint WM_INPUT_DEVICE_CHANGE = 0x00FE;
    public const uint WM_INPUT = 0x00FF;
    public const uint WM_POWERBROADCAST = 0x0218;
    public const uint WM_WTSSESSION_CHANGE = 0x02B1;
    public const uint WM_APP = 0x8000;

    // Power broadcast events
    public const int PBT_APMRESUMESUSPEND = 0x0007;
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;

    // Terminal services session change reasons (wParam of WM_WTSSESSION_CHANGE)
    public const int WTS_CONSOLE_CONNECT = 1;
    public const int WTS_CONSOLE_DISCONNECT = 2;
    public const int WTS_REMOTE_CONNECT = 3;
    public const int WTS_REMOTE_DISCONNECT = 4;

    // Raw Input
    public const uint RID_HEADER = 0x10000005;
    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDI_DEVICEINFO = 0x2000000B;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_DEVNOTIFY = 0x00002000;
    public const uint RIM_TYPEKEYBOARD = 1;
    public const uint RIM_TYPEHID = 2;

    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;
    public const uint NOTIFY_FOR_THIS_SESSION = 0;
    public const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

    /// <summary>Returned by the Raw Input functions on failure ((UINT)-1).</summary>
    public const uint RawInputError = uint.MaxValue;

    /// <summary>GUID_LIDSWITCH_STATE_CHANGE: data 0 = closed, 1 = open.</summary>
    public static readonly Guid LidSwitchStateChange = new("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");

    /// <summary>GUID_ACDC_POWER_SOURCE: data 0 = AC, 1 = DC (battery), 2 = short-term (UPS).</summary>
    public static readonly Guid AcDcPowerSource = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");

    public delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST
    {
        public IntPtr Device;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO_HID
    {
        public uint VendorId;
        public uint ProductId;
        public uint VersionNumber;
        public ushort UsagePage;
        public ushort Usage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO_KEYBOARD
    {
        public uint Type;
        public uint SubType;
        public uint KeyboardMode;
        public uint NumberOfFunctionKeys;
        public uint NumberOfIndicators;
        public uint NumberOfKeysTotal;
    }

    /// <summary>RID_DEVICE_INFO: the union is as large as its largest member (the keyboard part), 32 bytes in all.</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct RID_DEVICE_INFO
    {
        [FieldOffset(0)] public uint cbSize;
        [FieldOffset(4)] public uint Type;
        [FieldOffset(8)] public RID_DEVICE_INFO_HID Hid;
        [FieldOffset(8)] public RID_DEVICE_INFO_KEYBOARD Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        // followed by Data[DataLength]
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    // ---- windowing ----

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(in WNDCLASSEXW windowClass);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterClassW(string className, IntPtr instance);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    public static partial IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial int GetMessageW(out MSG message, IntPtr hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in MSG message);

    [LibraryImport("user32.dll")]
    public static partial IntPtr DispatchMessageW(in MSG message);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandleW(string? moduleName);

    // ---- raw input ----

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterRawInputDevices(RAWINPUTDEVICE* devices, uint count, uint size);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetRawInputData(IntPtr rawInput, uint command, void* data, ref uint size, uint headerSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetRawInputDeviceInfoW(IntPtr device, uint command, void* data, ref uint size);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetRawInputDeviceList(RAWINPUTDEVICELIST* list, ref uint count, uint size);

    // ---- power / session ----

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr RegisterPowerSettingNotification(IntPtr recipient, in Guid powerSetting, int flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterPowerSettingNotification(IntPtr handle);

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSRegisterSessionNotification(IntPtr hwnd, uint flags);

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSUnRegisterSessionNotification(IntPtr hwnd);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [LibraryImport("kernel32.dll")]
    public static partial uint WTSGetActiveConsoleSessionId();

    // ---- files ----

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveFileExW(string existingFileName, string? newFileName, uint flags);
}
