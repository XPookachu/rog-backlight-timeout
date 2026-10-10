using System.Xml.Linq;

namespace RogBacklightTimeout.Tests;

public sealed class PlatformGatingTests
{
    [Theory]
    [InlineData("ROG Strix G16 G614PR_G614PR")]
    [InlineData("ROG Zephyrus G14 GA403UV")]
    [InlineData("ROG Flow X13 GV302XU")]
    [InlineData("ROG Strix SCAR 17 G733PY")]
    public void Rog_laptops_are_allowed(string product) =>
        Assert.Null(new PlatformInfo("ASUSTeK COMPUTER INC.", "ROG", product).UnsupportedModelReason);

    [Theory]
    [InlineData("ROG Ally RC71L")]
    [InlineData("ROG Ally X RC72LA")]
    [InlineData("TUF Gaming A15 FA507NV")]
    [InlineData("ASUS Vivobook Pro 16 K6602VU")]
    [InlineData("Zenbook 14 OLED UX3405MA")]
    [InlineData("ProArt Studiobook 16 H7604JV")]
    [InlineData("ASUS EXPERTBOOK B9400")]
    public void Models_that_use_another_backlight_mechanism_are_refused(string product)
    {
        var platform = new PlatformInfo("ASUSTeK COMPUTER INC.", "", product);

        Assert.NotNull(platform.UnsupportedModelReason);
        var backlight = new RogBacklight(platform);
        Assert.False(backlight.Probe().Supported);
        Assert.False(backlight.Probe().Retryable);
        Assert.Throws<InvalidOperationException>(() => backlight.SetLevel(1));
    }

    [Fact]
    public void Ally_controller_ids_are_not_in_the_allow_list()
    {
        Assert.False(RogAuraProducts.IsKnown(0x1ABE));
        Assert.False(RogAuraProducts.IsKnown(0x1B4C));
    }
}

public sealed class TaskXmlTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Theory]
    [InlineData(true, "HighestAvailable")]
    [InlineData(false, "LeastPrivilege")]
    public void Builds_a_valid_task_with_the_requested_run_level(bool elevated, string runLevel)
    {
        var doc = XDocument.Parse(TaskXml.Build(@"PC\me", @"C:\Program Files\RogBacklightTimeout\RogBacklightTimeout.exe", elevated));

        Assert.Equal(runLevel, doc.Descendants(Ns + "RunLevel").Single().Value);
        Assert.Equal(@"C:\Program Files\RogBacklightTimeout\RogBacklightTimeout.exe", doc.Descendants(Ns + "Command").Single().Value);
        Assert.Equal("--agent", doc.Descendants(Ns + "Arguments").Single().Value);
        Assert.Equal("false", doc.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal("false", doc.Descendants(Ns + "StopIfGoingOnBatteries").Single().Value);
        Assert.Equal("PT0S", doc.Descendants(Ns + "ExecutionTimeLimit").Single().Value);
        Assert.NotNull(doc.Descendants(Ns + "LogonTrigger").SingleOrDefault());
    }

    [Fact]
    public void Special_characters_in_user_and_path_are_escaped()
    {
        var doc = XDocument.Parse(TaskXml.Build(@"DOMAIN\a&b<c>", @"C:\R&D\x.exe", elevated: true));

        Assert.Equal(@"DOMAIN\a&b<c>", doc.Descendants(Ns + "UserId").First().Value);
        Assert.Equal(@"C:\R&D\x.exe", doc.Descendants(Ns + "Command").Single().Value);
    }
}

public sealed class RawDeviceTests
{
    [Fact]
    public void Only_keyboards_and_precision_touchpads_are_relevant()
    {
        Assert.True(new RawDevice(1, 1, "kb", 0, 0).IsRelevant);                // keyboard
        Assert.True(new RawDevice(2, 2, "tp", 0x0D, 0x05).IsTouchpad);          // precision touchpad digitizer
        Assert.False(new RawDevice(3, 2, "vendor", 0xFF31, 0x76).IsRelevant);   // vendor-defined HID (ASUS hotkeys)
        Assert.False(new RawDevice(4, 2, "pen", 0x0D, 0x02).IsRelevant);        // pen digitizer
        Assert.False(new RawDevice(5, 0, "mouse", 0, 0).IsRelevant);            // mice are deliberately not listened to
    }
}
