namespace RogBacklightTimeout;

/// <summary>Everything Plug and Play tells us about one raw-input device.</summary>
/// <param name="InterfacePath">The Raw Input device path (a device interface path).</param>
/// <param name="InstanceId">Device instance ID, e.g. HID\VID_0B05&amp;PID_19B6&amp;MI_00&amp;COL02\8&amp;...</param>
/// <param name="ContainerId">DEVPKEY_Device_ContainerId; null when unavailable.</param>
/// <param name="RemovalPolicy">DEVPKEY_Device_RemovalPolicy (1 = expected no removal, 3 = surprise removal OK); informational.</param>
/// <param name="AncestorInstanceIds">Instance IDs of the parents, nearest first (e.g. USB function, USB device, root hub, PCI...).</param>
public sealed record DeviceFacts(
    string InterfacePath,
    string? InstanceId,
    Guid? ContainerId,
    uint? RemovalPolicy,
    IReadOnlyList<string> AncestorInstanceIds);

public enum VerdictReason
{
    OverrideInternal,
    OverrideExternal,
    NoPnpInfo,
    SeparateContainer,
    VirtualOrBluetoothParent,
    NoPlatformBus,
    BuiltInHardware,
    FallbackNoBuiltInKeyboard,
}

public readonly record struct Verdict(bool IsInternal, VerdictReason Reason)
{
    public string Describe() => Reason switch
    {
        VerdictReason.OverrideInternal => "config override: internal",
        VerdictReason.OverrideExternal => "config override: external",
        VerdictReason.NoPnpInfo => "no Plug and Play information",
        VerdictReason.SeparateContainer => "own device container (removable peripheral)",
        VerdictReason.VirtualOrBluetoothParent => "virtual or Bluetooth device",
        VerdictReason.NoPlatformBus => "not connected to the computer's own hardware (no ACPI/PCI parent)",
        VerdictReason.BuiltInHardware => "part of this computer (local machine container)",
        VerdictReason.FallbackNoBuiltInKeyboard => "fallback: no built-in keyboard detected, counting all hardware",
        _ => Reason.ToString(),
    };
}

/// <summary>User overrides: case-insensitive substrings matched against the interface path and instance ID.</summary>
public sealed record DeviceOverrides(IReadOnlyList<string> Internal, IReadOnlyList<string> External)
{
    public static DeviceOverrides None { get; } = new([], []);
}

/// <summary>
/// Pure decision logic: is this device built into the computer?
///
/// Windows assigns every device a container ID. Non-removable devices that are part of the machine share the
/// well-known "local machine container" {00000000-0000-0000-FFFF-FFFFFFFFFFFF}; removable devices get their own
/// container. ACPI _UPC/_PLD data tells Windows which USB ports are internal, so an internal keyboard behind an
/// internal USB hub is also in the local machine container.
/// https://learn.microsoft.com/windows-hardware/drivers/install/overview-of-container-ids
/// https://learn.microsoft.com/windows-hardware/drivers/install/using-acpi-for-device-container-grouping
/// </summary>
public static class DeviceClassifier
{
    public static readonly Guid LocalMachineContainer = new("00000000-0000-0000-FFFF-FFFFFFFFFFFF");

    // Enumerators of the real platform hardware: once the parent chain reaches one of these, the device is
    // physically part of the machine. Anything virtual or wireless on the way (before that) disqualifies it.
    private static readonly HashSet<string> PlatformBuses = new(StringComparer.OrdinalIgnoreCase) { "ACPI", "PCI" };
    private static readonly HashSet<string> VirtualOrWireless = new(StringComparer.OrdinalIgnoreCase)
    {
        "ROOT", "SWD", "BTH", "BTHENUM", "BTHLE",
    };

    public static Verdict Classify(string interfacePath, DeviceFacts? facts, DeviceOverrides overrides)
    {
        if (Matches(overrides.External, interfacePath, facts?.InstanceId))
            return new Verdict(false, VerdictReason.OverrideExternal);
        if (Matches(overrides.Internal, interfacePath, facts?.InstanceId))
            return new Verdict(true, VerdictReason.OverrideInternal);

        if (facts?.ContainerId is not { } container)
            return new Verdict(false, VerdictReason.NoPnpInfo);
        if (container != LocalMachineContainer)
            return new Verdict(false, VerdictReason.SeparateContainer);

        // Walk device -> parents. Built-in means the chain reaches the platform's own ACPI/PCI hardware without passing
        // through anything virtual or wireless; a chain that never reaches it gives no evidence, so it is not built-in.
        var chain = (facts.InstanceId is null ? facts.AncestorInstanceIds : facts.AncestorInstanceIds.Prepend(facts.InstanceId));
        foreach (var id in chain)
        {
            var enumerator = EnumeratorOf(id);
            if (VirtualOrWireless.Contains(enumerator))
                return new Verdict(false, VerdictReason.VirtualOrBluetoothParent);
            if (PlatformBuses.Contains(enumerator))
                return new Verdict(true, VerdictReason.BuiltInHardware);
        }

        return new Verdict(false, VerdictReason.NoPlatformBus);
    }

    /// <summary>"USB\VID_0B05&amp;PID_19B6\..." -> "USB".</summary>
    public static string EnumeratorOf(string instanceId)
    {
        var slash = instanceId.IndexOf('\\');
        return slash < 0 ? instanceId : instanceId[..slash];
    }

    private static bool Matches(IReadOnlyList<string> patterns, string interfacePath, string? instanceId)
    {
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            if (interfacePath.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return true;
            if (instanceId is not null && instanceId.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
