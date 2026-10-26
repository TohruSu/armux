using System.Text.Json;
using System.Text.Json.Serialization;

namespace Armux.Core;

/// <summary>User preferences, saved as JSON in %LOCALAPPDATA%\armux\settings.json.</summary>
public sealed record Settings
{
    /// <summary>Zed's default buffer font. armux bundles it (SIL OFL-1.1, ARCHITECTURE.md section 10).</summary>
    public const string DefaultTerminalFont = "Lilex";
    /// <summary>Zed's default interface font (".ZedSans"). armux bundles it (SIL OFL-1.1).</summary>
    public const string DefaultUiFont = "IBM Plex Sans";
    public const double DefaultTerminalFontSize = 13, MinFontSize = 8, MaxFontSize = 32;

    // Settable, not init: the JSON source generator fills init-only properties missing from the file with
    // default values (0, null), which would turn a partial settings file into font size 0.
    public string TerminalFont { get; set; } = DefaultTerminalFont;
    public double TerminalFontSize { get; set; } = DefaultTerminalFontSize;
    public string UiFont { get; set; } = DefaultUiFont;
    /// <summary>Shell for new terminals: "pwsh" (PowerShell) or "cmd".</summary>
    public string Shell { get; set; } = "pwsh";
    /// <summary>Folder for the first terminal and new workspaces; empty means the user profile. "~" is allowed.</summary>
    public string StartDirectory { get; set; } = "";
    /// <summary>Selecting text with the mouse copies it (Warp, Windows Terminal's copyOnSelect).</summary>
    public bool CopyOnSelect { get; set; } = true;

    /// <summary>Blank names fall back to the defaults; sizes are clamped and rounded to half points.</summary>
    public Settings Normalized() => this with
    {
        TerminalFont = string.IsNullOrWhiteSpace(TerminalFont) ? DefaultTerminalFont : TerminalFont.Trim(),
        UiFont = string.IsNullOrWhiteSpace(UiFont) ? DefaultUiFont : UiFont.Trim(),
        Shell = Pty.Shells.Name(Pty.Shells.Parse(Shell)),
        StartDirectory = (StartDirectory ?? "").Trim().Trim('"'),
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
