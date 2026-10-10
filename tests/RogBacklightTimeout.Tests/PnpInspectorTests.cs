namespace RogBacklightTimeout.Tests;

public sealed class PnpInspectorTests
{
    [Theory]
    [InlineData("")]
    [InlineData(@"\\?\HID#this-device-does-not-exist#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}")]
    public void Unknown_paths_yield_null_instead_of_throwing(string path) =>
        Assert.Null(PnpInspector.Inspect(path));

    [Fact]
    public void Every_device_this_machine_reports_can_be_inspected_without_throwing()
    {
        foreach (var device in RawInputDevices.Enumerate())
        {
            var facts = PnpInspector.Inspect(device.Path);
            if (facts is null) continue;
            Assert.NotEmpty(facts.AncestorInstanceIds);
            Assert.NotNull(facts.InstanceId);
        }
    }
}