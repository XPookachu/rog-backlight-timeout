namespace RogBacklightTimeout.Tests;

public sealed class SettingsTests
{
    private sealed class SilentLog : ILog
    {
        public void Info(string message) { }
    }

    [Fact]
    public void Timeout_and_level_are_clamped()
    {
        var settings = new Settings { TimeoutSeconds = 1, FallbackLevel = 9 }.Normalized();
        Assert.Equal(Settings.MinTimeoutSeconds, settings.TimeoutSeconds);
        Assert.Equal(3, settings.FallbackLevel);
    }

    [Fact]
    public void Blank_override_entries_are_dropped()
    {
        var settings = new Settings { InternalDeviceOverrides = ["", "  ", "VID_1"], ExternalDeviceOverrides = [null!] }.Normalized();
        Assert.Equal(["VID_1"], settings.InternalDeviceOverrides);
        Assert.Empty(settings.ExternalDeviceOverrides);
    }

    [Fact]
    public void Save_then_load_round_trips_and_bad_json_gives_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bi-{Guid.NewGuid():N}.json");
        try
        {
            SettingsStore.Save(new Settings { Enabled = false, TimeoutSeconds = 45, InternalDeviceOverrides = ["X"] }, path);
            var loaded = SettingsStore.Load(path);
            Assert.False(loaded.Enabled);
            Assert.Equal(45, loaded.TimeoutSeconds);
            Assert.Equal(["X"], loaded.InternalDeviceOverrides);

            File.WriteAllText(path, "{ not json");
            Assert.True(SettingsStore.Load(path, new SilentLog()).Enabled); // defaults, and nothing is written to the real log
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Saved_config_contains_only_user_facing_keys()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bi-{Guid.NewGuid():N}.json");
        try
        {
            SettingsStore.Save(new Settings(), path);
            var keys = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(["enabled", "timeoutSeconds", "fallbackLevel", "gHelperConfigPath", "internalDeviceOverrides", "externalDeviceOverrides"], keys);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Config_files_from_older_versions_with_unknown_keys_still_load()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bi-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "timeoutSeconds": 12, "internalDevicePatterns": ["VID_0B05&PID_19B6"] }""");
            Assert.Equal(12, SettingsStore.Load(path).TimeoutSeconds);
        }
        finally { File.Delete(path); }
    }
}

public sealed class RogAuraProductsTests
{
    [Fact]
    public void Product_ids_are_unique_and_include_the_verified_models()
    {
        Assert.Equal(RogAuraProducts.ProductIds.Count, RogAuraProducts.ProductIds.Distinct().Count());
        Assert.True(RogAuraProducts.IsKnown(0x19B6)); // ROG Strix G16 (2025), verified on hardware
        Assert.True(RogAuraProducts.IsKnown(0x1866)); // ROG N-KEY, also in the Linux hid-asus driver
        Assert.False(RogAuraProducts.IsKnown(0x1B1C)); // Corsair, not ASUS
    }

    [Fact]
    public void Platform_detection_recognises_asus_only()
    {
        Assert.True(new PlatformInfo("ASUSTeK COMPUTER INC.", "ROG Strix G16", "G614PR").IsAsus);
        Assert.False(new PlatformInfo("LENOVO", "Legion", "82JU").IsAsus);
        Assert.False(new PlatformInfo("", "", "").IsAsus);
    }

    [Fact]
    public void Backlight_refuses_non_asus_systems_without_touching_hardware()
    {
        var backlight = new RogBacklight(new PlatformInfo("Dell Inc.", "XPS", "9530"));

        Assert.False(backlight.Probe().Supported);
        Assert.Throws<InvalidOperationException>(() => backlight.SetLevel(1));
    }
}

public sealed class GHelperLevelSourceTests : IDisposable
{
    private sealed class NullLog : ILog
    {
        public void Info(string message) { }
    }

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"gh-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    private GHelperLevelSource Source(bool onBattery, int fallback = 2) =>
        new(() => new Settings { GHelperConfigPath = _path, FallbackLevel = fallback }, () => onBattery, new NullLog());

    [Fact]
    public void Uses_the_plugged_in_key_on_ac_and_the_ac_suffixed_key_on_battery()
    {
        // G-Helper quirk: "keyboard_brightness_ac" is used while on BATTERY (InputDispatcher.GetBacklight).
        File.WriteAllText(_path, """{ "keyboard_brightness": 3, "keyboard_brightness_ac": 1 }""");

        Assert.Equal(3, Source(onBattery: false).GetRestoreLevel());
        Assert.Equal(1, Source(onBattery: true).GetRestoreLevel());
    }

    [Fact]
    public void Missing_key_means_ghelpers_default_of_1()
    {
        File.WriteAllText(_path, "{ }");
        Assert.Equal(1, Source(onBattery: false).GetRestoreLevel());
    }

    [Theory]
    [InlineData("""{ "keyboard_brightness": 9 }""", 3)]
    [InlineData("""{ "keyboard_brightness": -4 }""", 0)]
    [InlineData("""{ "keyboard_brightness": "2" }""", 2)]
    public void Values_are_parsed_leniently_and_clamped(string json, int expected)
    {
        File.WriteAllText(_path, json);
        Assert.Equal(expected, Source(onBattery: false).GetRestoreLevel());
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("")]
    public void Unreadable_config_falls_back(string content)
    {
        File.WriteAllText(_path, content);
        Assert.Equal(2, Source(onBattery: false, fallback: 2).GetRestoreLevel());
    }

    [Fact]
    public void Reports_ghelpers_own_keyboard_timeouts_so_a_conflict_can_be_flagged()
    {
        File.WriteAllText(_path, """{ "keyboard_timeout": 60, "keyboard_ac_timeout": 0 }""");
        Assert.Equal((60, 0), Source(onBattery: false).ReadGHelperTimeouts());

        File.WriteAllText(_path, "{ }");
        Assert.Equal((-1, -1), Source(onBattery: false).ReadGHelperTimeouts()); // absent = off

        File.Delete(_path);
        Assert.Null(Source(onBattery: false).ReadGHelperTimeouts());
    }
    [Fact]
    public void Missing_file_falls_back_and_stamp_is_zero()
    {
        Assert.Equal(2, Source(onBattery: false).GetRestoreLevel());
        Assert.Equal(0, Source(onBattery: false).Stamp);
    }

    [Fact]
    public void Stamp_changes_when_the_file_is_rewritten()
    {
        File.WriteAllText(_path, "{ }");
        var source = Source(onBattery: false);
        var before = source.Stamp;

        File.SetLastWriteTimeUtc(_path, DateTime.UtcNow.AddMinutes(5));

        Assert.NotEqual(before, source.Stamp);
    }
}
