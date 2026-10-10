namespace RogBacklightTimeout;

/// <summary>
/// USB product IDs (vendor 0x0B05, ASUS) of ROG/ASUS "Aura" keyboard controllers whose backlight brightness can
/// be set with the HID feature report documented in <see cref="RogBacklight"/>.
///
/// Source of truth: G-Helper's MAIN_AURA_PIDS in app/USB/AsusHid.cs, synced from
/// https://github.com/seerge/g-helper/blob/8f0382d683ad4a1fedb8545da2392b0e45e4e155/app/USB/AsusHid.cs (2026-09-13).
/// Cross-reference: the Linux kernel's hid-asus driver and asusctl handle the same controllers
/// (drivers/hid/hid-asus.c, USB_DEVICE_ID_ASUSTEK_ROG_NKEY_KEYBOARD = 0x1866, ..._KEYBOARD2 = 0x19b6).
///
/// Deliberately NOT included although G-Helper lists them: 0x1ABE and 0x1B4C (ROG Ally / Ally X). G-Helper drives the Ally
/// with report 0x5D plus an Aura apply, and its gamepad input is neither keyboard nor touchpad, so this agent would
/// dim the lights while playing. The ROG Ally product family is also rejected by <see cref="PlatformInfo"/>.
///
/// To add a model: add its product ID here and a row to the table test. Never add an ID that has not been
/// verified in G-Helper (or on real hardware): the agent only ever writes to IDs listed here.
/// </summary>
public static class RogAuraProducts
{
    public const int AsusVendorId = 0x0B05;

    public static IReadOnlyList<int> ProductIds { get; } =
    [
        0x1A30, 0x1854, 0x1869, 0x1866, 0x19B6, 0x1822, 0x1837, 0x184A, 0x183D, 0x8502, 0x1807,
        0x17E0, 0x1B6E, 0x1B2C, 0x8854, 0x1CE7, 0x1CE6, 0x1BF2, 0x1BF3, 0x1CD7, 0x1CD8,
    ];

    public static bool IsKnown(int productId) => ProductIds.Contains(productId);
}
