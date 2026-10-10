using System.Text.Json;
using System.Text.Json.Serialization;

namespace RogBacklightTimeout;

/// <summary>User-editable agent configuration (config.json).</summary>
public sealed record Settings
{
    public const int MinTimeoutSeconds = 5;
    public const int MaxTimeoutSeconds = 3600;

    public bool Enabled { get; init; } = true;

    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Level (0-3) used to restore the backlight when G-Helper's config can't be read.</summary>
    public int FallbackLevel { get; init; } = 2;

    /// <summary>Optional explicit path to G-Helper's config.json (auto-detected when empty).</summary>
    public string GHelperConfigPath { get; init; } = "";

    /// <summary>
    /// Escape hatch for unusual hardware: case-insensitive substrings of a device path / instance ID that must
    /// be treated as built-in even if automatic detection disagrees. Normally empty (see 'doctor').
    /// </summary>
    public string[] InternalDeviceOverrides { get; init; } = [];

    /// <summary>Substrings of device paths / instance IDs that must never count as built-in input.</summary>
    public string[] ExternalDeviceOverrides { get; init; } = [];

    [JsonIgnore]
    public DeviceOverrides Overrides => new(InternalDeviceOverrides, ExternalDeviceOverrides);

    public Settings Normalized() => this with
    {
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds),
        FallbackLevel = Math.Clamp(FallbackLevel, 0, 3),
        GHelperConfigPath = GHelperConfigPath ?? "",
        InternalDeviceOverrides = Clean(InternalDeviceOverrides),
        ExternalDeviceOverrides = Clean(ExternalDeviceOverrides),
    };

    private static string[] Clean(string[]? values) =>
        (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

public static class SettingsStore
{
    public static Settings Load(string? path = null, ILog? log = null)
    {
        path ??= AppPaths.ConfigFile;
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var loaded = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.Settings);
                if (loaded is not null) return loaded.Normalized();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            (log ?? Log.Instance).Error($"Could not read config, using defaults: {ex.Message}");
        }
        return new Settings().Normalized();
    }

    public static void Save(Settings settings, string? path = null)
    {
        path ??= AppPaths.ConfigFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings.Normalized(), SettingsJsonContext.Default.Settings));
        File.Move(tmp, path, overwrite: true);
    }
}
