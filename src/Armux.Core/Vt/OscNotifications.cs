using System.Text;

namespace Armux.Core.Vt;

/// <summary>A desktop notification requested by a program through OSC 9, 99 or 777. <see cref="Title"/> may be empty.</summary>
public sealed record TermNotification(string Title, string Body);

/// <summary>
/// Turns OSC payloads into notifications, as cmux does:
/// <list type="bullet">
/// <item>OSC 9 (iTerm2): <c>9;body</c>. ConEmu's numeric subcommands (<c>9;4;...</c> progress and others) are ignored.</item>
/// <item>OSC 777 (rxvt / Ghostty): <c>777;notify;title;body</c>.</item>
/// <item>OSC 99 (kitty): <c>99;metadata;payload</c>, with chunks joined by id (<c>i</c>), <c>d=0</c> for "more to come",
/// <c>p=title|body</c> and <c>e=1</c> for base64.</item>
/// </list>
/// </summary>
public sealed class OscNotifications
{
    const int MaxPending = 16, MaxText = 4096;
    readonly Dictionary<string, (StringBuilder Title, StringBuilder Body)> pending = [];

    /// <summary>The notification <paramref name="osc"/> completes, or null.</summary>
    public TermNotification? Handle(string osc)
    {
        int semi = osc.IndexOf(';');
        if (semi < 0) return null;
        string code = osc[..semi], rest = osc[(semi + 1)..];
        return code switch
        {
            "9" => Osc9(rest),
            "777" => Osc777(rest),
            "99" => Osc99(rest),
            _ => null,
        };
    }

    static TermNotification? Osc9(string body)
    {
        // ConEmu uses OSC 9;<n>;... for progress, sleep, cwd and others.
        int i = 0;
        while (i < body.Length && char.IsAsciiDigit(body[i])) i++;
        if (i > 0 && (i == body.Length || body[i] == ';')) return null;
        return body.Length == 0 ? null : new TermNotification("", Clip(body));
    }

    static TermNotification? Osc777(string rest)
    {
        var parts = rest.Split(';', 3);
        if (parts[0] != "notify" || parts.Length < 2) return null;
        string title = parts[1], body = parts.Length > 2 ? parts[2] : "";
        return Make(title, body);
    }

    TermNotification? Osc99(string rest)
    {
        int semi = rest.IndexOf(';');
        if (semi < 0) return null;
        string meta = rest[..semi], payload = rest[(semi + 1)..];
        string id = "0", part = "title";
        bool done = true, base64 = false;
        foreach (var kv in meta.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq < 0) continue;
            string k = kv[..eq], v = kv[(eq + 1)..];
            switch (k)
            {
                case "i": id = v; break;
                case "d": done = v != "0"; break;
                case "p": part = v; break;
                case "e": base64 = v == "1"; break;
            }
        }
        if (part is not ("title" or "body")) return null;   // icons, buttons and queries are not supported
        if (base64)
        {
            try { payload = Encoding.UTF8.GetString(Convert.FromBase64String(payload)); }
            catch (FormatException) { return null; }
        }
        if (!pending.TryGetValue(id, out var entry))
        {
            if (pending.Count >= MaxPending) pending.Clear();
            pending[id] = entry = (new StringBuilder(), new StringBuilder());
        }
        var target = part == "title" ? entry.Title : entry.Body;
        if (target.Length + payload.Length <= MaxText) target.Append(payload);
        if (!done) return null;
        pending.Remove(id);
        return Make(entry.Title.ToString(), entry.Body.ToString());
    }

    /// <summary>A title without a body is shown as the body, so every notification has text.</summary>
    static TermNotification? Make(string title, string body)
    {
        if (body.Length == 0 && title.Length == 0) return null;
        return body.Length == 0 ? new TermNotification("", Clip(title)) : new TermNotification(Clip(title), Clip(body));
    }

    static string Clip(string s) => s.Length <= MaxText ? s : s[..MaxText];
}
