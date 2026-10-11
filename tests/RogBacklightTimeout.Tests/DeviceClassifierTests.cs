namespace RogBacklightTimeout.Tests;

/// <summary>
/// Fixtures are real devices captured from a ROG Strix G16 (G614PR) with Get-PnpDeviceProperty,
/// plus synthetic cases for situations that machine does not have (Bluetooth, virtual, docks).
/// </summary>
public sealed class DeviceClassifierTests
{
    private static readonly Guid Local = DeviceClassifier.LocalMachineContainer;
    private static readonly Guid UsbPeripheral = Guid.Parse("D811BC24-86CD-11F1-93C4-CE2A4CEC7264");

    // Built-in keyboard: USB device on an internal (non-removable) port.
    private static readonly DeviceFacts InternalUsbKeyboard = new(
        @"\\?\HID#VID_0B05&PID_19B6&MI_00&Col02#8&3a3892e0&0&0001#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}",
        @"HID\VID_0B05&PID_19B6&MI_00&COL02\8&3A3892E0&0&0001", Local, 1,
        [
            @"USB\VID_0B05&PID_19B6&MI_00\7&1389c7ea&0&0000",
            @"USB\VID_0B05&PID_19B6\6&731cc08&0&1",
            @"USB\ROOT_HUB30\5&89d3634&0&0",
            @"PCI\VEN_1022&DEV_15B6&SUBSYS_20AF1043&REV_00\4&1d975b9a&0&0341",
            @"PCI\VEN_1022&DEV_14DD&SUBSYS_10431074&REV_00\3&11583659&0&41",
            @"ACPI\PNP0A08\0",
        ]);

    // Built-in touchpad: I2C HID enumerated by ACPI.
    private static readonly DeviceFacts InternalTouchpad = new(
        @"\\?\HID#ASUP1203&Col02#4&312a035&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}",
        @"HID\ASUP1203&COL02\4&312A035&0&0001", Local, 1,
        [@"ACPI\ASUP1203\1", @"ACPI\AMDI0010\0", @"ACPI_HAL\PNP0C08\0", @"ROOT\ACPI_HAL\0000", @"HTREE\ROOT\0"]);

    // External USB keyboard: separate container, surprise-removal policy.
    private static readonly DeviceFacts ExternalUsbKeyboard = new(
        @"\\?\HID#VID_1A2C&PID_9400&MI_00#8&305b525d&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}",
        @"HID\VID_1A2C&PID_9400&MI_00\8&305B525D&0&0000", UsbPeripheral, 3,
        [
            @"USB\VID_1A2C&PID_9400&MI_00\7&1b419f46&1&0000",
            @"USB\VID_1A2C&PID_9400\6&3428b9c&0&1",
            @"USB\ROOT_HUB30\5&2bdf3a97&0&0",
            @"PCI\VEN_1022&DEV_15B7&SUBSYS_20AF1043&REV_00\4&1d975b9a&0&0441",
        ]);

    [Fact]
    public void Built_in_usb_keyboard_is_internal()
    {
        var verdict = DeviceClassifier.Classify(InternalUsbKeyboard.InterfacePath, InternalUsbKeyboard, DeviceOverrides.None);
        Assert.True(verdict.IsInternal);
        Assert.Equal(VerdictReason.BuiltInHardware, verdict.Reason);
    }

    [Fact]
    public void Built_in_acpi_touchpad_is_internal_even_though_its_chain_ends_in_root()
    {
        var verdict = DeviceClassifier.Classify(InternalTouchpad.InterfacePath, InternalTouchpad, DeviceOverrides.None);
        Assert.True(verdict.IsInternal);
    }

    [Fact]
    public void External_usb_keyboard_is_external()
    {
        var verdict = DeviceClassifier.Classify(ExternalUsbKeyboard.InterfacePath, ExternalUsbKeyboard, DeviceOverrides.None);
        Assert.False(verdict.IsInternal);
        Assert.Equal(VerdictReason.SeparateContainer, verdict.Reason);
    }

    [Fact]
    public void Device_without_pnp_info_is_external()
    {
        var verdict = DeviceClassifier.Classify(@"\\?\HID#whatever", null, DeviceOverrides.None);
        Assert.False(verdict.IsInternal);
        Assert.Equal(VerdictReason.NoPnpInfo, verdict.Reason);
    }

    [Fact]
    public void Virtual_device_in_the_local_container_is_external()
    {
        var facts = new DeviceFacts(@"\\?\HID#virtualdevice&10#2&17e4fba6&0&0000#{884b96c3}", @"HID\VIRTUALDEVICE&10\2&17E4FBA6&0&0000",
            Local, 1, [@"ROOT\SYSTEM\0001", @"HTREE\ROOT\0"]);

        var verdict = DeviceClassifier.Classify(facts.InterfacePath, facts, DeviceOverrides.None);

        Assert.False(verdict.IsInternal);
        Assert.Equal(VerdictReason.VirtualOrBluetoothParent, verdict.Reason);
    }

    [Theory]
    [InlineData(@"BTHENUM\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002046D_PID&B023\7&1&0&C0")]
    [InlineData(@"BTHLE\DEV_C0AABBCCDDEE\8&2&0&C0")]
    [InlineData(@"SWD\DRIVERENUM\SOMETHING")]
    public void Bluetooth_and_software_parents_are_external_even_in_the_local_container(string parent)
    {
        var facts = new DeviceFacts(@"\\?\HID#x", @"HID\X\1", Local, 1, [parent, @"PCI\VEN_8086&DEV_1\1"]);

        var verdict = DeviceClassifier.Classify(facts.InterfacePath, facts, DeviceOverrides.None);

        Assert.False(verdict.IsInternal);
    }

    [Fact]
    public void External_override_wins_over_everything()
    {
        var overrides = new DeviceOverrides(["VID_0B05"], ["PID_19B6"]);
        var verdict = DeviceClassifier.Classify(InternalUsbKeyboard.InterfacePath, InternalUsbKeyboard, overrides);
        Assert.False(verdict.IsInternal);
        Assert.Equal(VerdictReason.OverrideExternal, verdict.Reason);
    }

    [Fact]
    public void Internal_override_rescues_a_misdetected_device_and_is_case_insensitive()
    {
        var overrides = new DeviceOverrides(["vid_1a2c&pid_9400"], []);
        var verdict = DeviceClassifier.Classify(ExternalUsbKeyboard.InterfacePath, ExternalUsbKeyboard, overrides);
        Assert.True(verdict.IsInternal);
        Assert.Equal(VerdictReason.OverrideInternal, verdict.Reason);
    }

    [Fact]
    public void Overrides_also_match_the_instance_id_and_work_without_pnp_info()
    {
        var byInstance = new DeviceOverrides(["ASUP1203"], []);
        Assert.True(DeviceClassifier.Classify(@"\\?\opaque", InternalTouchpad with { InterfacePath = @"\\?\opaque", ContainerId = UsbPeripheral }, byInstance).IsInternal);
        Assert.True(DeviceClassifier.Classify(@"\\?\HID#ASUP1203&Col02#x", null, byInstance).IsInternal);
    }

    [Fact]
    public void A_chain_that_never_reaches_platform_hardware_is_not_evidence_of_built_in()
    {
        var facts = new DeviceFacts(@"\\?\HID#x", @"HID\X\1", Local, 1, [@"HTREE\ROOT\0"]);
        var verdict = DeviceClassifier.Classify(facts.InterfacePath, facts, DeviceOverrides.None);
        Assert.False(verdict.IsInternal);
        Assert.Equal(VerdictReason.NoPlatformBus, verdict.Reason);
    }

    [Fact]
    public void A_software_enumerated_device_itself_is_external_even_with_a_hardware_looking_parent()
    {
        var facts = new DeviceFacts(@"\\?\SWD#x", @"SWD\X\1", Local, 1, [@"PCI\VEN_8086&DEV_1\1"]);
        Assert.False(DeviceClassifier.Classify(facts.InterfacePath, facts, DeviceOverrides.None).IsInternal);
    }
    [Theory]
    [InlineData(@"USB\VID_0B05&PID_19B6\6&731cc08&0&1", "USB")]
    [InlineData(@"ACPI_HAL\PNP0C08\0", "ACPI_HAL")]
    [InlineData("NOSLASH", "NOSLASH")]
    public void Enumerator_is_the_prefix_of_the_instance_id(string instanceId, string expected) =>
        Assert.Equal(expected, DeviceClassifier.EnumeratorOf(instanceId));
}

public sealed class InputClassifierTests
{
    private sealed class NullLog : ILog
    {
        public void Info(string message) { }
    }

    private static readonly Guid Peripheral = Guid.Parse("D811BC24-86CD-11F1-93C4-CE2A4CEC7264");

    private static DeviceFacts Facts(string path, Guid container) => new(path, path, container, null, [@"PCI\X\1"]);

    [Fact]
    public void Without_any_built_in_keyboard_all_hardware_counts_instead_of_nothing()
    {
        var external = @"\\?\HID#VID_1A2C&PID_9400&MI_00#a";
        var classifier = new InputClassifier(() => DeviceOverrides.None, p => Facts(p, Peripheral), () => [external], new NullLog());
        classifier.Refresh();

        Assert.False(classifier.HasBuiltInKeyboard);
        var verdict = classifier.Classify(external);
        Assert.True(verdict.IsInternal);
        Assert.Equal(VerdictReason.FallbackNoBuiltInKeyboard, verdict.Reason);
    }

    [Fact]
    public void With_a_built_in_keyboard_external_devices_stay_external()
    {
        var builtIn = @"\\?\HID#builtin";
        var external = @"\\?\HID#external";
        var classifier = new InputClassifier(
            () => DeviceOverrides.None,
            p => Facts(p, p == builtIn ? DeviceClassifier.LocalMachineContainer : Peripheral),
            () => [builtIn, external],
            new NullLog());
        classifier.Refresh();

        Assert.True(classifier.HasBuiltInKeyboard);
        Assert.True(classifier.Classify(builtIn).IsInternal);
        Assert.False(classifier.Classify(external).IsInternal);
    }

    [Fact]
    public void A_failed_or_empty_enumeration_keeps_the_previous_state()
    {
        var keyboards = new List<string> { @"\\?\HID#external" };
        var classifier = new InputClassifier(() => DeviceOverrides.None, p => Facts(p, Peripheral), () => keyboards, new NullLog());
        classifier.Refresh();
        Assert.False(classifier.HasBuiltInKeyboard);

        keyboards.Clear();
        classifier.Refresh();

        Assert.False(classifier.HasBuiltInKeyboard); // unchanged, not reset to "all good"
    }

    [Fact]
    public void Fallback_switches_off_again_when_a_built_in_keyboard_appears()
    {
        var keyboards = new List<string> { @"\\?\HID#external" };
        var classifier = new InputClassifier(
            () => DeviceOverrides.None,
            p => Facts(p, p.Contains("builtin") ? DeviceClassifier.LocalMachineContainer : Peripheral),
            () => keyboards, new NullLog());
        classifier.Refresh();
        Assert.True(classifier.Classify(@"\\?\HID#external").IsInternal); // fallback active

        keyboards.Add(@"\\?\HID#builtin");
        classifier.Refresh();

        Assert.True(classifier.HasBuiltInKeyboard);
        Assert.False(classifier.Classify(@"\\?\HID#external").IsInternal);
    }
    [Fact]
    public void Fallback_also_counts_devices_whose_lookup_failed()
    {
        var failed = @"\\?\HID#lookup-failed";
        var classifier = new InputClassifier(() => DeviceOverrides.None, p => p == failed ? null : Facts(p, Peripheral), () => [@"\\?\HID#external", failed], new NullLog());
        classifier.Refresh();

        Assert.False(classifier.HasBuiltInKeyboard);
        Assert.True(classifier.Classify(failed).IsInternal);
    }

    [Fact]
    public void Explicit_external_override_is_never_rescued_by_the_fallback()
    {
        var path = @"\\?\HID#VID_1A2C&PID_9400";
        var classifier = new InputClassifier(() => new DeviceOverrides([], ["VID_1A2C"]), p => Facts(p, Peripheral), () => [path], new NullLog());
        classifier.Refresh();

        Assert.False(classifier.Classify(path).IsInternal);
    }
}
