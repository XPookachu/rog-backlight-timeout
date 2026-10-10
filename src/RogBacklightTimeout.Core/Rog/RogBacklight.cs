using HidSharp;
using HidSharp.Reports;

namespace RogBacklightTimeout;

/// <param name="Retryable">Unsupported right now but may work later (the controller may simply not be enumerated yet at logon).</param>
public readonly record struct ProbeResult(bool Supported, string Detail, bool Retryable = false)
{
    public override string ToString() => $"{(Supported ? "supported" : "NOT supported")}: {Detail}";
}

/// <summary>
/// Sets keyboard backlight brightness on ROG laptops exactly like G-Helper does
/// (app/USB/AsusHid.cs WriteInput + app/USB/Aura.cs DirectBrightness): HID feature report
/// [0x5A, 0xBA, 0xC5, 0xC4, level] with level 0..3, sent to the ASUS Aura keyboard controller.
/// Only brightness is changed, so G-Helper's lighting mode/colours/speed keep running and are simply hidden
/// while the level is 0.
///
/// Safety: the agent only writes to (a) an ASUS computer whose model G-Helper also drives this way (not Ally/TUF/Vivobook/Zenbook...),
/// (b) devices whose product ID is in
/// <see cref="RogAuraProducts"/> and (c) which expose feature report 0x5A in their report descriptor.
/// The payload bytes are constants; configuration can never change them.
/// </summary>
public sealed class RogBacklight : IBacklight
{
    private const byte InputReportId = 0x5A;
    private const int MaxLevel = 3;

    private static readonly object HidLock = new();
    private readonly PlatformInfo _platform;

    public RogBacklight(PlatformInfo platform) => _platform = platform;

    /// <summary>Read-only check: no report is sent.</summary>
    public ProbeResult Probe()
    {
        if (!_platform.IsAsus)
            return new ProbeResult(false, $"not an ASUS computer ({_platform.Manufacturer})");

        if (_platform.UnsupportedModelReason is { } modelReason)
            return new ProbeResult(false, modelReason);

        var devices = FindDevices().ToList();
        if (devices.Count == 0)
            return new ProbeResult(false, "no known ASUS Aura keyboard controller with feature report 0x5A found (yet)", Retryable: true);

        var list = string.Join(", ", devices.Select(d => $"PID_{d.ProductID:X4}"));
        return new ProbeResult(true, $"{devices.Count} controller(s): {list}");
    }

    public void SetLevel(int level)
    {
        if (!_platform.IsAsus) throw new InvalidOperationException("Not an ASUS computer");
        if (_platform.UnsupportedModelReason is { } modelReason) throw new InvalidOperationException(modelReason);

        level = Math.Clamp(level, 0, MaxLevel);
        byte[] message = [InputReportId, 0xBA, 0xC5, 0xC4, (byte)level];

        lock (HidLock)
        {
            var written = 0;
            Exception? last = null;
            foreach (var device in FindDevices())
            {
                try
                {
                    using var stream = device.Open();
                    var payload = new byte[device.GetMaxFeatureReportLength()];
                    Array.Copy(message, payload, message.Length);
                    stream.SetFeature(payload);
                    written++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
                {
                    last = ex; // like G-Helper: keep trying the other devices
                }
            }

            if (written == 0)
                throw new InvalidOperationException(last?.Message ?? "No ASUS Aura keyboard controller exposing report 0x5A found");
        }
    }

    public static IEnumerable<HidDevice> FindDevices()
    {
        foreach (var device in DeviceList.Local.GetHidDevices(RogAuraProducts.AsusVendorId))
        {
            if (!RogAuraProducts.IsKnown(device.ProductID)) continue;

            bool usable;
            try
            {
                usable = device.GetMaxFeatureReportLength() > 0
                    && device.GetReportDescriptor().TryGetReport(ReportType.Feature, InputReportId, out _);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException)
            {
                usable = false;
            }

            if (usable) yield return device;
        }
    }
}
