namespace RogBacklightTimeout.Tests;

public sealed class InputMonitorTests
{
    private sealed class FakeClassifier : IInputClassifier
    {
        public Verdict Next { get; set; } = new(false, VerdictReason.NoPnpInfo);
        public int Calls { get; private set; }

        public Verdict Classify(string interfacePath)
        {
            Calls++;
            return Next;
        }

        public void Refresh() { }
    }

    private static readonly IntPtr Device = new(42);

    private static (InputMonitor Monitor, FakeClassifier Classifier, Func<long> Advance, List<string> Seen) Create()
    {
        long now = 1000;
        var classifier = new FakeClassifier();
        var seen = new List<string>();
        var monitor = new InputMonitor(classifier, (path, verdict) => seen.Add($"{path}:{verdict.Reason}"), _ => @"\\?\HID#x", () => now);
        return (monitor, classifier, () => now += InputMonitor.RetryMilliseconds + 1, seen);
    }

    [Fact]
    public void A_good_verdict_is_looked_up_once()
    {
        var (monitor, classifier, _, _) = Create();
        classifier.Next = new Verdict(true, VerdictReason.BuiltInHardware);

        Assert.True(monitor.IsInternal(Device));
        Assert.True(monitor.IsInternal(Device));

        Assert.Equal(1, classifier.Calls);
    }

    [Fact]
    public void A_failed_lookup_is_retried_after_the_delay_and_not_cached()
    {
        var (monitor, classifier, advance, seen) = Create();

        Assert.False(monitor.IsInternal(Device)); // lookup failed
        Assert.False(monitor.IsInternal(Device)); // within the delay: no new lookup
        Assert.Equal(1, classifier.Calls);

        classifier.Next = new Verdict(true, VerdictReason.BuiltInHardware); // the lookup works now
        advance();

        Assert.True(monitor.IsInternal(Device));
        Assert.Equal(2, classifier.Calls);
        Assert.Equal([$@"\\?\HID#x:{VerdictReason.NoPnpInfo}", $@"\\?\HID#x:{VerdictReason.BuiltInHardware}"], seen);
    }

    [Fact]
    public void Repeated_failures_are_reported_once()
    {
        var (monitor, _, advance, seen) = Create();

        monitor.IsInternal(Device);
        advance();
        monitor.IsInternal(Device);
        advance();
        monitor.IsInternal(Device);

        Assert.Single(seen);
    }

    [Fact]
    public void Invalidate_forgets_good_verdicts()
    {
        var (monitor, classifier, _, _) = Create();
        classifier.Next = new Verdict(true, VerdictReason.BuiltInHardware);
        monitor.IsInternal(Device);

        monitor.Invalidate();
        monitor.IsInternal(Device);

        Assert.Equal(2, classifier.Calls);
    }

    [Fact]
    public void A_null_handle_is_never_internal()
    {
        var (monitor, classifier, _, _) = Create();
        Assert.False(monitor.IsInternal(IntPtr.Zero));
        Assert.Equal(0, classifier.Calls);
    }
}

public sealed class ConsoleSessionTests
{
    [Theory]
    [InlineData(1u, 1u, true)]
    [InlineData(2u, 1u, false)]            // another user (or a remote session) holds the console
    [InlineData(0xFFFFFFFFu, 1u, false)]   // nobody attached right now
    [InlineData(0xFFFFFFFFu, 0xFFFFFFFFu, false)]
    public void Only_the_session_that_owns_the_console_controls_the_keyboard(uint console, uint own, bool expected) =>
        Assert.Equal(expected, MessageWindow.IsOnConsole(console, own));
}
