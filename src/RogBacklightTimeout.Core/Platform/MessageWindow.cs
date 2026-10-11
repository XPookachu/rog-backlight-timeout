using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RogBacklightTimeout;

/// <summary>
/// A hidden top-level window and its message loop: the single place where Windows talks to the agent.
/// It decodes Raw Input, device arrival/removal, resume, power source, lid, session and shutdown messages into plain
/// events. Everything is raised on the thread that calls <see cref="Run"/>.
///
/// Why a hidden top-level window and not a message-only window: message-only windows never receive broadcast
/// messages (WM_POWERBROADCAST, WM_QUERYENDSESSION, WM_ENDSESSION).
/// https://devblogs.microsoft.com/oldnewthing/20171218-00/?p=97595
///
/// Privacy: only the Raw Input header is read. The agent never sees which key was pressed or any touch data.
/// The mouse usage is deliberately not registered: precision-touchpad cursor events arrive with a null device
/// handle, and external mice would only generate noise. https://learn.microsoft.com/windows/win32/inputdev/raw-input
/// </summary>
internal sealed unsafe class MessageWindow : IDisposable
{
    private const string ClassName = "RogBacklightTimeout.MessageWindow";

    private readonly Native.WndProc _wndProc; // kept alive for the lifetime of the window class
    private readonly ConcurrentQueue<Action> _posted = new();
    private IntPtr _hwnd;
    private IntPtr _className;
    private IntPtr _lidNotification;
    private IntPtr _powerSourceNotification;
    private bool _sessionNotification;
    private bool _onConsole = true;

    private readonly Action<string>? _warn;

    /// <param name="warn">Receives a message when a Windows notification could not be registered.</param>
    public MessageWindow(Action<string>? warn = null)
    {
        _wndProc = WindowProc;
        _warn = warn;
    }

    /// <summary>A keyboard or touchpad event; the argument is the Raw Input device handle (zero = injected/virtual).</summary>
    public event Action<IntPtr>? RawInput;

    public event Action? DevicesChanged;
    public event Action? Resumed;
    public event Action<bool>? PowerSourceChanged;    // true = battery
    public event Action<bool>? LidChanged;            // true = closed
    public event Action<bool>? SessionDisconnected;   // true = lost the console, false = regained
    public event Action? SessionEnding;               // may still be cancelled
    public event Action? SessionEnded;

    /// <summary>Queue work to run on the message-loop thread; safe to call from any thread.</summary>
    public void Post(Action action)
    {
        _posted.Enqueue(action);
        if (_hwnd != IntPtr.Zero) Native.PostMessageW(_hwnd, Native.WM_APP, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Ask the loop to end (safe from any thread).</summary>
    public void Quit()
    {
        if (_hwnd != IntPtr.Zero) Native.PostMessageW(_hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Creates the window and pumps messages until <see cref="Quit"/>.</summary>
    public void Run()
    {
        Create();
        while (Native.GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            Native.TranslateMessage(in message);
            Native.DispatchMessageW(in message);
        }
    }

    private void Create()
    {
        var instance = Native.GetModuleHandleW(null);
        _className = Marshal.StringToHGlobalUni(ClassName);
        var windowClass = new Native.WNDCLASSEXW
        {
            cbSize = (uint)sizeof(Native.WNDCLASSEXW),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = _className,
        };
        if (Native.RegisterClassExW(in windowClass) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed");

        _hwnd = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW, ClassName, "RogBacklightTimeout", Native.WS_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed");

        RegisterRawInput();
        // Registration failures are not fatal (input still works) but leave the lid, AC/battery or session safeguard
        // without events: log them.
        _lidNotification = Native.RegisterPowerSettingNotification(_hwnd, Native.LidSwitchStateChange, Native.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (_lidNotification == IntPtr.Zero) _warn?.Invoke($"Could not register for lid notifications (error {Marshal.GetLastWin32Error()})");
        _powerSourceNotification = Native.RegisterPowerSettingNotification(_hwnd, Native.AcDcPowerSource, Native.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (_powerSourceNotification == IntPtr.Zero) _warn?.Invoke($"Could not register for power source notifications (error {Marshal.GetLastWin32Error()})");
        _sessionNotification = Native.WTSRegisterSessionNotification(_hwnd, Native.NOTIFY_FOR_THIS_SESSION);
        if (!_sessionNotification) _warn?.Invoke($"Could not register for session notifications (error {Marshal.GetLastWin32Error()})");

        CheckConsole(); // started inside a remote session or while another user holds the console

        // Work queued before the window existed.
        if (!_posted.IsEmpty) Native.PostMessageW(_hwnd, Native.WM_APP, IntPtr.Zero, IntPtr.Zero);
    }

    private void RegisterRawInput()
    {
        const uint flags = Native.RIDEV_INPUTSINK | Native.RIDEV_DEVNOTIFY;
        var devices = stackalloc Native.RAWINPUTDEVICE[]
        {
            new() { UsagePage = 0x01, Usage = 0x06, Flags = flags, Target = _hwnd }, // keyboard
            new() { UsagePage = 0x0D, Usage = 0x05, Flags = flags, Target = _hwnd }, // precision touchpad
        };
        if (!Native.RegisterRawInputDevices(devices, 2, (uint)sizeof(Native.RAWINPUTDEVICE)))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterRawInputDevices failed");
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case Native.WM_INPUT:
                    HandleInput(lParam);
                    break; // fall through to DefWindowProc: required so the system can clean up the raw input buffer
                case Native.WM_INPUT_DEVICE_CHANGE:
                    DevicesChanged?.Invoke();
                    break;
                case Native.WM_APP:
                    while (_posted.TryDequeue(out var action)) action();
                    return IntPtr.Zero;
                case Native.WM_POWERBROADCAST:
                    HandlePowerBroadcast((int)wParam, lParam);
                    return (IntPtr)1; // TRUE
                case Native.WM_WTSSESSION_CHANGE:
                    HandleSessionChange((int)wParam);
                    return IntPtr.Zero;
                case Native.WM_QUERYENDSESSION:
                    SessionEnding?.Invoke();
                    return (IntPtr)1; // never veto a logoff or shutdown
                case Native.WM_ENDSESSION:
                    if (wParam != IntPtr.Zero) SessionEnded?.Invoke();
                    return IntPtr.Zero;
                case Native.WM_DESTROY:
                    Teardown();
                    Native.PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            // An exception must never cross the native boundary: it would take the agent down.
            Log.Instance.Error($"Message handler failed: {ex.Message}");
        }
        return Native.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void HandleInput(IntPtr lParam)
    {
        Native.RAWINPUTHEADER header;
        var size = (uint)sizeof(Native.RAWINPUTHEADER);
        if (Native.GetRawInputData(lParam, Native.RID_HEADER, &header, ref size, size) == uint.MaxValue) return;
        if (header.Type != Native.RIM_TYPEKEYBOARD && header.Type != Native.RIM_TYPEHID) return;
        RawInput?.Invoke(header.Device);
    }

    private void HandlePowerBroadcast(int eventType, IntPtr lParam)
    {
        switch (eventType)
        {
            case Native.PBT_APMRESUMEAUTOMATIC:
            case Native.PBT_APMRESUMESUSPEND:
                Resumed?.Invoke();
                break;
            case Native.PBT_POWERSETTINGCHANGE:
                var setting = (Native.POWERBROADCAST_SETTING*)lParam;
                if (setting->DataLength < sizeof(int)) return;
                var value = *(int*)((byte*)setting + sizeof(Native.POWERBROADCAST_SETTING));
                if (setting->PowerSetting == Native.LidSwitchStateChange) LidChanged?.Invoke(value == 0);
                else if (setting->PowerSetting == Native.AcDcPowerSource) PowerSourceChanged?.Invoke(value != 0);
                break;
        }
    }

    /// <summary>
    /// True when this session is the one at the physical keyboard. 0xFFFFFFFF means no session is attached to the
    /// console right now (e.g. mid-switch), which is also "not ours".
    /// </summary>
    internal static bool IsOnConsole(uint consoleSession, uint ownSession) =>
        consoleSession != 0xFFFFFFFF && consoleSession == ownSession;

    private static bool OnConsoleNow() =>
        IsOnConsole(Native.WTSGetActiveConsoleSessionId(), (uint)System.Diagnostics.Process.GetCurrentProcess().SessionId);

    /// <summary>
    /// Connect/disconnect messages only say that something changed; the console owner is looked up each time, so a
    /// remote connect no longer counts as "got the console back". Raised only when the answer changes.
    /// </summary>
    private void HandleSessionChange(int reason)
    {
        if (reason is not (Native.WTS_CONSOLE_CONNECT or Native.WTS_CONSOLE_DISCONNECT
                           or Native.WTS_REMOTE_CONNECT or Native.WTS_REMOTE_DISCONNECT)) return;
        CheckConsole();
    }

    private void CheckConsole()
    {
        var onConsole = OnConsoleNow();
        if (onConsole == _onConsole) return;
        _onConsole = onConsole;
        SessionDisconnected?.Invoke(!onConsole);
    }

    private void Teardown()
    {
        if (_lidNotification != IntPtr.Zero) Native.UnregisterPowerSettingNotification(_lidNotification);
        if (_powerSourceNotification != IntPtr.Zero) Native.UnregisterPowerSettingNotification(_powerSourceNotification);
        if (_sessionNotification) Native.WTSUnRegisterSessionNotification(_hwnd);
        _lidNotification = _powerSourceNotification = IntPtr.Zero;
        _sessionNotification = false;
        if (_className != IntPtr.Zero) { Marshal.FreeHGlobal(_className); _className = IntPtr.Zero; }
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero) Native.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }
}
