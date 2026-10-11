namespace RogBacklightTimeout;

/// <summary>
/// Maps Raw Input device handles to a built-in/external verdict and remembers it, so the Plug and Play lookup
/// happens once per device instead of once per keystroke. Not thread-safe: use it from the message-loop thread.
/// A verdict that only says "the lookup failed" (<see cref="VerdictReason.NoPnpInfo"/>) is not remembered:
/// it is retried (at most every <see cref="RetryMilliseconds"/>) so a temporary failure cannot stick.
/// </summary>
internal sealed class InputMonitor
{
    internal const long RetryMilliseconds = 2000;

    private readonly IInputClassifier _classifier;
    private readonly Action<string, Verdict>? _onNewDevice;
    private readonly Func<IntPtr, string> _getPath;
    private readonly Func<long> _now;
    private readonly Dictionary<IntPtr, Verdict> _verdicts = new();
    private readonly Dictionary<IntPtr, long> _retryAt = new();

    public InputMonitor(
        IInputClassifier classifier,
        Action<string, Verdict>? onNewDevice = null,
        Func<IntPtr, string>? getPath = null,
        Func<long>? now = null)
    {
        _classifier = classifier;
        _onNewDevice = onNewDevice;
        _getPath = getPath ?? RawInputDevices.GetPath;
        _now = now ?? (() => Environment.TickCount64);
    }

    /// <summary>True when the event came from a built-in keyboard or touchpad. A null handle means injected/virtual input.</summary>
    public bool IsInternal(IntPtr device)
    {
        if (device == IntPtr.Zero) return false;
        if (_verdicts.TryGetValue(device, out var known)) return known.IsInternal;

        var now = _now();
        var seenBefore = _retryAt.TryGetValue(device, out var retryAt);
        if (seenBefore && now < retryAt) return false;

        var path = _getPath(device);
        var verdict = _classifier.Classify(path);
        if (verdict.Reason == VerdictReason.NoPnpInfo)
        {
            _retryAt[device] = now + RetryMilliseconds;
            if (!seenBefore) _onNewDevice?.Invoke(path, verdict); // report once, not on every retry
            return false;
        }

        _verdicts[device] = verdict;
        _retryAt.Remove(device);
        _onNewDevice?.Invoke(path, verdict);
        return verdict.IsInternal;
    }

    /// <summary>Forget cached verdicts (configuration changed, or devices came and went).</summary>
    public void Invalidate()
    {
        _verdicts.Clear();
        _retryAt.Clear();
    }
}
