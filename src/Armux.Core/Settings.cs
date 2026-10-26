using System.Text.Json;
using System.Text.Json.Serialization;

namespace Armux.Core;

/// <summary>User preferences, saved as JSON in %LOCALAPPDATA%\armux\settings.json.</summary>
public sealed record Settings
{
    /// <summary>Zed's default buffer font. armux bundles it (SIL OFL-1.1, ARCHITECTURE.md section 10).</summary>
    public const string DefaultTerminalFont = "Lilex";
    public const string DefaultUiFont = "Segoe UI Variable Text";
    public const double DefaultTerminalFontSize = 13, MinFontSize = 8, MaxFontSize = 32;

    // Settable, not init: the JSON source generator fills init-only properties missing from the file with
    // default values (0, null), which would turn a partial settings file into font size 0.
    public string TerminalFont { get; set; } = DefaultTerminalFont;
    public double TerminalFontSize { get; set; } = DefaultTerminalFontSize;
    public string UiFont { get; set; } = DefaultUiFont;

    /// <summary>Blank names fall back to the defaults; sizes are clamped and rounded to half points.</summary>
    public Settings Normalized() => this with
    {
        TerminalFont = string.IsNullOrWhiteSpace(TerminalFont) ? DefaultTerminalFont : TerminalFont.Trim(),
        UiFont = string.IsNullOrWhiteSpace(UiFont) ? DefaultUiFont : UiFont.Trim(),
        TerminalFontSize = double.IsFinite(TerminalFontSize)
            ? Math.Clamp(Math.Round(TerminalFontSize * 2) / 2, MinFontSize, MaxFontSize)
            : DefaultTerminalFontSize,
    };

    public Settings WithTerminalFontSize(double size) => (this with { TerminalFontSize = size }).Normalized();

    /// <summary>Reads settings from <paramref name="path"/>. A missing or unreadable file gives the defaults.</summary>
    public static Settings Load(string path)
    {
        try
        {
            return (JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJson.Default.Settings) ?? new()).Normalized();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, SettingsJson.Default.Settings));
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
