namespace Armux.Core;

/// <summary>A palette entry. <see cref="Shortcut"/> is shown inline, as in cmux's palette.</summary>
public sealed record Command(string Id, string Title, string Shortcut, Action Run);

public static class CommandSearch
{
    /// <summary>
    /// Subsequence match with a score: consecutive hits and word starts score higher.
    /// Returns null when <paramref name="query"/> is not a subsequence of <paramref name="text"/>.
    /// </summary>
    public static int? Score(string text, string query)
    {
        if (query.Length == 0) return 0;
        int score = 0, ti = 0, run = 0;
        foreach (char qc in query)
        {
            if (qc == ' ') continue;
            int found = -1;
            for (; ti < text.Length; ti++)
            {
                if (char.ToLowerInvariant(text[ti]) == char.ToLowerInvariant(qc)) { found = ti; ti++; break; }
                run = 0;
            }
            if (found < 0) return null;
            run++;
            score += 1 + run * 2;
            if (found == 0) score += 12;   // prefix of the whole title
            else if (text[found - 1] is ' ' or '-' or ':') score += 6;
        }
        return score - text.Length / 8;
    }

    public static IEnumerable<Command> Filter(IEnumerable<Command> all, string query) =>
        all.Select(c => (c, s: Score(c.Title, query.Trim())))
           .Where(x => x.s is not null)
           .OrderByDescending(x => x.s)
           .ThenBy(x => x.c.Title, StringComparer.OrdinalIgnoreCase)
           .Select(x => x.c);
}

/// <summary>A key chord such as Ctrl+Shift+D. Key names follow Windows virtual-key names.</summary>
public readonly record struct Chord(bool Ctrl, bool Alt, bool Shift, string Key)
{
    public static Chord Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        // "Ctrl+=" splits into ["Ctrl", ""], and "Ctrl+Shift++" would too; treat a trailing empty part as "+".
        string key = parts[^1].Length == 0 ? "+" : parts[^1];
        var mods = parts[..^1];
        return new Chord(
            mods.Contains("Ctrl", StringComparer.OrdinalIgnoreCase),
            mods.Contains("Alt", StringComparer.OrdinalIgnoreCase),
            mods.Contains("Shift", StringComparer.OrdinalIgnoreCase),
            key.ToUpperInvariant());
    }

    public override string ToString() =>
        string.Join("+", new[] { Ctrl ? "Ctrl" : null, Alt ? "Alt" : null, Shift ? "Shift" : null, Key }.Where(s => s is not null));
}
