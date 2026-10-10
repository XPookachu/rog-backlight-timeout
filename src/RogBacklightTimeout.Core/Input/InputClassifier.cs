namespace RogBacklightTimeout;

public interface IInputClassifier
{
    Verdict Classify(string interfacePath);

    /// <summary>Re-evaluate environment-dependent state (call on start and when devices come or go).</summary>
    void Refresh();
}

/// <summary>
/// Combines Plug and Play facts, the pure <see cref="DeviceClassifier"/> rules and the user's overrides.
/// Safety net: if no keyboard at all is recognised as built-in (unusual hardware or firmware), treating
/// everything as external would mean nothing could ever wake the backlight. In that case all hardware
/// devices count, which degrades to G-Helper's own behaviour instead of failing.
/// </summary>
public sealed class InputClassifier : IInputClassifier
{
    private readonly Func<DeviceOverrides> _overrides;
    private readonly Func<string, DeviceFacts?> _inspect;
    private readonly Func<IEnumerable<string>> _keyboardPaths;
    private readonly ILog _log;
    private volatile bool _hasBuiltInKeyboard = true;

    public InputClassifier(
        Func<DeviceOverrides> overrides,
        Func<string, DeviceFacts?> inspect,
        Func<IEnumerable<string>> keyboardPaths,
        ILog log)
    {
        _overrides = overrides;
        _inspect = inspect;
        _keyboardPaths = keyboardPaths;
        _log = log;
    }

    /// <summary>False when no keyboard looks built-in and the fallback (count all hardware) is active.</summary>
    public bool HasBuiltInKeyboard => _hasBuiltInKeyboard;

    public Verdict Classify(string interfacePath)
    {
        var verdict = ClassifyStrict(interfacePath);
        if (!_hasBuiltInKeyboard && verdict.Reason == VerdictReason.SeparateContainer)
            return new Verdict(true, VerdictReason.FallbackNoBuiltInKeyboard);
        return verdict;
    }

    public void Refresh()
    {
        var keyboards = _keyboardPaths().ToList();
        if (keyboards.Count == 0) return; // enumeration failed or nothing present: keep the previous state

        var found = keyboards.Any(path => ClassifyStrict(path).IsInternal);
        if (found != _hasBuiltInKeyboard)
        {
            _log.Info(found
                ? "Built-in keyboard detected"
                : "WARNING: no built-in keyboard detected; counting all hardware input until one appears (see 'doctor')");
        }
        _hasBuiltInKeyboard = found;
    }

    private Verdict ClassifyStrict(string interfacePath) =>
        DeviceClassifier.Classify(interfacePath, _inspect(interfacePath), _overrides());
}
