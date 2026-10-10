namespace RogBacklightTimeout;

public static class PowerStatus
{
    private const byte AcOnline = 1;

    /// <summary>
    /// True unless Windows reports AC power. This is the same rule G-Helper applies
    /// (<c>PowerLineStatus != PowerLineStatus.Online</c> in InputDispatcher.GetBacklight): unknown counts as battery.
    /// </summary>
    public static bool IsOnBattery() =>
        !(Native.GetSystemPowerStatus(out var status) && status.ACLineStatus == AcOnline);
}
