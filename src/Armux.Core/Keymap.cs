namespace Armux.Core;

/// <summary>
/// Shortcuts: each command's default chord, overridden by <see cref="Settings.Keybindings"/>. Command ids are the
/// palette titles, so a settings file stays readable ("Split Right": "Ctrl+Shift+D").
/// </summary>
public sealed class Keymap
{
    readonly Dictionary<string, string> defaults = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> overrides;

    public Keymap(IReadOnlyDictionary<string, string> overrides) =>
        this.overrides = new Dictionary<string, string>(overrides, StringComparer.Ordinal);

    public void AddDefault(string id, string chord) => defaults[id] = chord;

    public IEnumerable<string> Ids => defaults.Keys;

    public string Default(string id) => defaults.GetValueOrDefault(id, "");

    /// <summary>The shortcut in effect: the override if there is one (possibly "" for none), else the default.</summary>
    public string Effective(string id) => overrides.TryGetValue(id, out var o) ? Normalize(o) : Default(id);

    public bool IsCustom(string id) => overrides.ContainsKey(id) && Normalize(overrides[id]) != Default(id);

    /// <summary>Other commands with the same effective shortcut.</summary>
    public IEnumerable<string> Conflicts(string id, string chord)
    {
        chord = Normalize(chord);
        if (chord.Length == 0) return [];
        return Ids.Where(other => other != id && Effective(other) == chord);
    }

    /// <summary>The overrides to save after setting <paramref name="id"/> to <paramref name="chord"/>; a default value drops the override.</summary>
    public Dictionary<string, string> With(string id, string? chord)
    {
        var result = new Dictionary<string, string>(overrides, StringComparer.Ordinal);
        if (chord is null || Normalize(chord) == Default(id)) result.Remove(id);
        else result[id] = Normalize(chord);
        return result;
    }

    /// <summary>"ctrl+shift+d" and "Shift+Ctrl+D" both become "Ctrl+Shift+D"; "" stays "".</summary>
    public static string Normalize(string chord) => chord.Trim().Length == 0 ? "" : Chord.Parse(chord).ToString();
}
