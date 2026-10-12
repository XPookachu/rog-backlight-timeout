namespace RogBacklightTimeout.Tests;

public sealed class InputSourcesTests
{
    private static string Path(string device, string collection, string guid = "{4d1e55b2-f16f-11cf-88cb-001111000030}") =>
        $@"\\?\HID#{device}{collection}#8&1234&0&0000#{guid}";

    private const string Kbd = "VID_0B05&PID_19B6&MI_00";
    private const string KeyboardGuid = "{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";

    private static RawDevice Hid(string device, string collection, ushort page, ushort usage) =>
        new(new IntPtr(1), RawInputDevices.TypeHid, Path(device, collection), page, usage);

    private static RawDevice Keyboard(string device, string collection) =>
        new(new IntPtr(2), RawInputDevices.TypeKeyboard, Path(device, collection, KeyboardGuid), 0, 0);

    // The built-in ASUS keyboard as Windows reports it on a ROG Strix G16, plus an external keyboard dongle.
    private static readonly RawDevice[] Laptop =
    [
        Keyboard(Kbd, "&Col02"),
        Hid(Kbd, "&Col03", 0xFF31, 0x76),
        Hid(Kbd, "&Col04", 0xFF31, 0x79),
        Hid(Kbd, "&Col05", 0x0C, 0x01),
        Hid(Kbd, "&Col06", 0x01, 0x0C),                  // radio controls: not a key channel
        Hid("ASUP1203", "&Col02", 0x0D, 0x05),           // touchpad
        Keyboard("VID_1A2C&PID_9400&MI_00", ""),
        Hid("VID_1A2C&PID_9400&MI_01", "&Col01", 0x0C, 0x01),
        Hid("VID_1A2C&PID_9400&MI_01", "&Col03", 0xFF00, 0x01),
        Hid("VID_1B1C&PID_1BF0&MI_02", "", 0xFF42, 0x02),
    ];

    private static bool BuiltIn(string path) => path.Contains("VID_0B05") || path.Contains("ASUP1203");

    [Fact]
    public void The_built_in_keyboards_fn_collections_are_listened_to()
    {
        var usages = InputSources.Select(Laptop, BuiltIn);

        Assert.Equal(
            [HidUsage.Keyboard, HidUsage.PrecisionTouchpad, new HidUsage(0xFF31, 0x76), new HidUsage(0xFF31, 0x79), HidUsage.ConsumerControl],
            usages);
    }

    [Fact]
    public void Other_devices_collections_and_unrelated_pages_are_ignored()
    {
        var usages = InputSources.Select(Laptop, BuiltIn);

        Assert.DoesNotContain(new HidUsage(0xFF00, 0x01), usages); // external keyboard's vendor collection
        Assert.DoesNotContain(new HidUsage(0xFF42, 0x02), usages); // external RGB/mouse
        Assert.DoesNotContain(new HidUsage(0x01, 0x0C), usages);   // radio controls on the keyboard itself
    }

    [Fact]
    public void Without_a_built_in_keyboard_only_the_defaults_remain()
    {
        var usages = InputSources.Select(Laptop, _ => false);
        Assert.Equal(InputSources.Always, usages);
    }

    [Fact]
    public void Duplicate_usages_are_listed_once()
    {
        RawDevice[] devices = [Keyboard(Kbd, "&Col02"), Hid(Kbd, "&Col03", 0xFF31, 0x76), Hid(Kbd, "&Col03", 0xFF31, 0x76)];
        Assert.Single(InputSources.Select(devices, BuiltIn), u => u == new HidUsage(0xFF31, 0x76));
    }

    [Theory]
    [InlineData(@"\\?\HID#VID_0B05&PID_19B6&MI_00&Col03#8&3a3892e0&0&0002#{4d1e55b2}", "VID_0B05&PID_19B6&MI_00")]
    [InlineData(@"\\?\HID#VID_0B05&PID_19B6&MI_00&Col02#8&3a3892e0&0&0001#{884b96c3}", "VID_0B05&PID_19B6&MI_00")]
    [InlineData(@"\\?\HID#VID_1A2C&PID_9400&MI_00#8&305b525d&0&0000#{884b96c3}", "VID_1A2C&PID_9400&MI_00")]
    [InlineData(@"\\?\HID#ASUP1203&Col02#4&312a035&0&0001#{4d1e55b2}", "ASUP1203")]
    public void Collections_of_one_device_share_a_hardware_key(string path, string expected) =>
        Assert.Equal(expected, InputSources.HardwareKey(path));
}
