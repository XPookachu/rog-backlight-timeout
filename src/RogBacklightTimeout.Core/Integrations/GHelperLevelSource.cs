using System.Diagnostics;
using System.Text.Json;

namespace RogBacklightTimeout;

/// <summary>
/// Reads the brightness G-Helper is configured to use, so the agent stays in sync with it.
/// Mirrors G-Helper's InputDispatcher.GetBacklight(): plugged in -> "keyboard_brightness",
/// on battery -> "keyboard_brightness_ac" (sic). Only read when dimming/restoring.
/// </summary>
public sealed class GHelperLevelSource : ILevelSource
{
    private const string KeyPluggedIn = "keyboard_brightness";
    private const string KeyBattery = "keyboard_brightness_ac";
    private const int GHelperDefaultLevel = 1;

    // G-Helper reads its config with comments and trailing commas allowed (AppConfig.LenientOptions).
    private static readonly JsonDocumentOptions LenientJson = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly Func<Settings> _settings;
    private readonly Func<bool> _onBattery;
    private readonly ILog _log;
    private string? _cachedPath;
    private string? _cachedFor;
    private long _cachedAt;

    public GHelperLevelSource(Func<Settings> settings, Func<bool> onBattery, ILog log)
    {
        _settings = settings;
        _onBattery = onBattery;
        _log = log;
    }

    public long Stamp
    {
        get
        {
            try
            {
                var path = CachedConfigPath(_settings());
                return path is null ? 0 : File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// G-Helper's own keyboard timeouts in seconds (battery, plugged in); 0 or less means off. If either is above 0 it
    /// fights this agent (its InitBacklightTimer only runs when the value is > 0; absent keys mean off).
    /// </summary>
    public (int Battery, int Plugged)? ReadGHelperTimeouts()
    {
        var path = CachedConfigPath(_settings());
        if (path is null) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream, LenientJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            int Read(string key) => doc.RootElement.TryGetProperty(key, out var v) ? ReadInt(v, -1) : -1;
            return (Read("keyboard_timeout"), Read("keyboard_ac_timeout"));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public int GetRestoreLevel()
    {
        var settings = _settings();
        try
        {
            var path = CachedConfigPath(settings);
            if (path is null) return settings.FallbackLevel;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var doc = JsonDocument.Parse(stream, LenientJson);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) break;

                    var key = _onBattery() ? KeyBattery : KeyPluggedIn;
                    if (!doc.RootElement.TryGetProperty(key, out var value)) return GHelperDefaultLevel;
                    return Math.Clamp(ReadInt(value, GHelperDefaultLevel), 0, 3);
                }
                catch (IOException) { Thread.Sleep(50); }
                catch (JsonException) { Thread.Sleep(50); }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // fall through to the fallback below
        }

        _log.Info($"Could not read G-Helper config, using fallback level {settings.FallbackLevel}");
        return settings.FallbackLevel;
    }

    private static int ReadInt(JsonElement value, int fallback) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt32(out var n) => n,
        JsonValueKind.String when int.TryParse(value.GetString(), out var n) => n,
        _ => fallback,
    };

    /// <summary>The path lookup inspects running processes, so remember it briefly (the timer polls while dimmed).</summary>
    private string? CachedConfigPath(Settings settings)
    {
        var now = Environment.TickCount64;
        if (_cachedPath is null || now - _cachedAt > 60_000 || !File.Exists(_cachedPath) || _cachedFor != settings.GHelperConfigPath)
        {
            _cachedPath = ResolveConfigPath(settings);
            _cachedFor = settings.GHelperConfigPath;
            _cachedAt = now;
        }
        return _cachedPath;
    }

    /// <summary>Explicit setting > portable config next to the running GHelper.exe > %APPDATA%\GHelper.</summary>
    internal static string? ResolveConfigPath(Settings settings)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.GHelperConfigPath))
            {
                var explicitPath = Environment.ExpandEnvironmentVariables(settings.GHelperConfigPath);
                return File.Exists(explicitPath) ? explicitPath : null;
            }

            foreach (var process in Process.GetProcessesByName("GHelper"))
            {
                try
                {
                    var dir = Path.GetDirectoryName(process.MainModule?.FileName);
                    var portable = dir is null ? null : Path.Combine(dir, "config.json");
                    if (portable is not null && File.Exists(portable)) return portable;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                finally { process.Dispose(); }
            }

            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GHelper", "config.json");
            return File.Exists(appData) ? appData : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
