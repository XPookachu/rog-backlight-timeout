using Microsoft.Win32;

namespace RogBacklightTimeout;

/// <summary>SMBIOS identity of this computer (what Windows exposes in the registry).</summary>
public sealed record PlatformInfo(string Manufacturer, string Family, string Product)
{
    public bool IsAsus => Manufacturer.Contains("ASUS", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Model families for which G-Helper does NOT set the backlight through the ROG HID report: the ROG Ally (report
    /// 0x5D + Aura apply) and TUF / Vivobook / Zenbook / ProArt / ExpertBook (ACPI). Mirrors G-Helper's IsAlly(), IsTUF()
    /// and IsVivoZenPro() in app/AppConfig.cs, which match against Win32_ComputerSystem.Model (= SystemProductName).
    /// </summary>
    private static readonly string[] OtherMechanismModels =
        ["RC7", "TUF", "TX Gaming", "TX Air", "Vivobook", "Zenbook", "ProArt", "EXPERTBOOK", " V16", "ASUSLaptop"];

    /// <summary>Why this model must not be driven through the ROG HID path, or null when it may be.</summary>
    public string? UnsupportedModelReason
    {
        get
        {
            var marker = OtherMechanismModels.FirstOrDefault(m => Product.Contains(m, StringComparison.OrdinalIgnoreCase));
            return marker is null ? null : $"model '{Product}' uses a different backlight mechanism (matches '{marker.Trim()}')";
        }
    }

    public override string ToString() =>
        $"{Manufacturer} | {(string.IsNullOrWhiteSpace(Family) ? "(no family)" : Family)} | {Product}";
}

public static class RogPlatform
{
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";

    public static PlatformInfo Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(BiosKey);
        return new PlatformInfo(
            Manufacturer: key?.GetValue("SystemManufacturer") as string ?? "",
            Family: key?.GetValue("SystemFamily") as string ?? "",
            Product: key?.GetValue("SystemProductName") as string ?? "");
    }
}
