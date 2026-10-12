using System.Text.RegularExpressions;

namespace RogBacklightTimeout;

/// <summary>A HID top-level collection to receive through Raw Input.</summary>
internal readonly record struct HidUsage(ushort Page, ushort Id)
{
    public static readonly HidUsage Keyboard = new(0x01, 0x06);
    public static readonly HidUsage PrecisionTouchpad = new(0x0D, 0x05);
    public static readonly HidUsage ConsumerControl = new(0x0C, 0x01);
}

/// <summary>
/// Decides which Raw Input collections the agent listens to.
///
/// Raw Input delivers events per top-level collection, and one physical keyboard exposes several. Typing arrives on
/// the standard keyboard collection, but the Fn row (power profile, screen brightness, ROG key, media keys...) is
/// handled by the keyboard's firmware and reported on other collections of the same device: the consumer-control
/// collection and ASUS' vendor-defined ones (usage page 0xFF31). Listening only to the standard collection therefore
/// misses those keys.
///
/// Rule: always the standard keyboard and the precision touchpad; in addition every consumer-control or vendor-defined
/// collection of the same hardware as a built-in keyboard. Tying it to the keyboard's own device (instead of every
/// vendor collection in the system) keeps unrelated chatty devices (sensors, RGB controllers, external peripherals)
/// out, and needs no per-key or per-model list. Events from these collections still go through the normal built-in
/// check, and only the Raw Input header is read, never the report contents.
/// </summary>
internal static partial class InputSources
{
    private const ushort VendorDefinedFirstPage = 0xFF00;

    /// <summary>Always listened to, whatever the hardware: typing and the precision touchpad.</summary>
    public static readonly IReadOnlyList<HidUsage> Always = [HidUsage.Keyboard, HidUsage.PrecisionTouchpad];

    public static IReadOnlyList<HidUsage> Select(IEnumerable<RawDevice> devices, Func<string, bool> isBuiltIn)
    {
        var all = devices.ToList();
        var keyboardHardware = all
            .Where(d => d.IsKeyboard && isBuiltIn(d.Path))
            .Select(d => HardwareKey(d.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var usages = new List<HidUsage>(Always);
        foreach (var device in all)
        {
            if (device.Type != RawInputDevices.TypeHid || !IsKeyChannel(device)) continue;
            if (!keyboardHardware.Contains(HardwareKey(device.Path))) continue;
            var usage = new HidUsage(device.UsagePage, device.Usage);
            if (!usages.Contains(usage)) usages.Add(usage);
        }
        return usages;
    }

    private static bool IsKeyChannel(RawDevice device) =>
        device.UsagePage == HidUsage.ConsumerControl.Page && device.Usage == HidUsage.ConsumerControl.Id
        || device.UsagePage >= VendorDefinedFirstPage;

    /// <summary>
    /// Identifies the physical device behind a collection: the collection suffix is dropped, so
    /// <c>\\?\HID#VID_0B05&amp;PID_19B6&amp;MI_00&amp;Col03#8&amp;...#{guid}</c> and the same device's <c>Col02</c> match.
    /// </summary>
    internal static string HardwareKey(string interfacePath)
    {
        var parts = interfacePath.Split('#');
        return parts.Length < 2 ? interfacePath : CollectionSuffix().Replace(parts[1], "");
    }

    [GeneratedRegex(@"&Col\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex CollectionSuffix();
}
