namespace RogBacklightTimeout;

/// <summary>
/// Maps Raw Input device handles to a built-in/external verdict and remembers it, so the Plug and Play lookup
/// happens once per device instead of once per keystroke. Not thread-safe: use it from the message-loop thread.
/// </summary>
internal sealed class InputMonitor
{
    private readonly IInputClassifier _classifier;
    private readonly Action<string, Verdict>? _onNewDevice;
    private readonly Dictionary<IntPtr, Verdict> _verdicts = new();

    public InputMonitor(IInputClassifier classifier, Action<string, Verdict>? onNewDevice = null)
    {
        _classifier = classifier;
        _onNewDevice = onNewDevice;
    }

    /// <summary>True when the event came from a built-in keyboard or touchpad. A null handle means injected/virtual input.</summary>
    public bool IsInternal(IntPtr device)
    {
        if (device == IntPtr.Zero) return false;
        if (_verdicts.TryGetValue(device, out var known)) return known.IsInternal;

        var path = RawInputDevices.GetPath(device);
        var verdict = _classifier.Classify(path);
        _verdicts[device] = verdict;
        _onNewDevice?.Invoke(path, verdict);
        return verdict.IsInternal;
    }

    /// <summary>Forget cached verdicts (configuration changed, or devices came and went).</summary>
    public void Invalidate() => _verdicts.Clear();
}
