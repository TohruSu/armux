using System.Text;
using Armux.Core;
using Armux.Core.Pty;
using Armux.Core.Vt;
using Microsoft.UI.Dispatching;

namespace Armux.App;

/// <summary>
/// The terminal behind one terminal surface: an emulator plus either a live shell in a pseudoconsole or, for the
/// mock scenarios, the surface's canned lines. Lives as long as the surface, independent of its view, so theme or
/// font changes rebuild the view without restarting the shell.
/// </summary>
public sealed class TerminalSession : IDisposable
{
    readonly Surface surface;
    readonly DispatcherQueue ui;
    readonly IReadOnlyDictionary<string, string> environment;
    readonly string? cwd;
    PtySession? pty;
    FileStream? ptyLog;
    int renderQueued;

    public Terminal Terminal { get; } = new(80, 24);
    public bool Started { get; private set; }
    public bool Exited { get; private set; }

    /// <summary>Raised on the UI thread when output arrived (coalesced: at most one pending call).</summary>
    public event Action? Changed;
    public event Action<TermNotification>? Notified;
    public event Action<string>? CwdChanged;
    public event Action<int>? ProcessExited;

    public TerminalSession(Surface surface, DispatcherQueue ui, string? cwd, IReadOnlyDictionary<string, string> environment)
    {
        this.surface = surface;
        this.ui = ui;
        this.cwd = cwd;
        this.environment = environment;
        Terminal.TitleChanged += t => ui.TryEnqueue(() => { if (t.Length > 0) surface.Title = TabTitle(t); });
        Terminal.Notified += n => ui.TryEnqueue(() => Notified?.Invoke(n));
        Terminal.CwdChanged += p => ui.TryEnqueue(() => CwdChanged?.Invoke(p));
        Terminal.Reply += r => pty?.Write(r);
    }

    /// <summary>Starts the shell (or replays mock lines) at the size of the first layout.</summary>
    public void Start(int cols, int rows)
    {
        if (Started) return;
        Started = true;
        Terminal.Resize(cols, rows);
        if (!surface.Live)
        {
            Terminal.Feed(MockToVt(surface.Lines));
            return;
        }
        try
        {
            var kind = Shells.Parse(surface.Shell);
            // Diagnostics: ARMUX_PTY_LOG=<file> records the raw ConPTY output and every resize.
            var log = System.Environment.GetEnvironmentVariable("ARMUX_PTY_LOG") is { Length: > 0 } path ? File.Open(path, FileMode.Append, FileAccess.Write, FileShare.Read) : null;
            ptyLog = log;
            Log($"[start {cols}x{rows}]");
            pty = PtySession.Start(ShellIntegration.CommandLine(kind), cwd, ShellIntegration.Environment(kind, environment), cols, rows,
                data => { if (log is not null) lock (log) log.Write(data.Span); Terminal.Feed(data.Span); QueueRender(); },
                code => ui.TryEnqueue(() => { Exited = true; ProcessExited?.Invoke(code); }));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            App.Log(nameof(TerminalSession), e);
            Terminal.Feed($"\x1b[31marmux: could not start {surface.Shell}: {e.Message}\x1b[0m\r\n");
        }
    }

    void QueueRender()
    {
        if (Interlocked.Exchange(ref renderQueued, 1) == 1) return;
        ui.TryEnqueue(DispatcherQueuePriority.Low, () => { Volatile.Write(ref renderQueued, 0); Changed?.Invoke(); });
    }

    void Log(string marker)
    {
        if (ptyLog is not { } log) return;
        lock (log) { log.Write(Encoding.UTF8.GetBytes(marker)); log.Flush(); }
    }

    public void Resize(int cols, int rows)
    {
        Log($"[resize {cols}x{rows}]");
        Terminal.Resize(cols, rows);
        pty?.Resize(cols, rows);
    }

    public void Send(string text) => pty?.Write(text);

    public void Dispose()
    {
        pty?.Dispose();
        pty = null;
        ptyLog?.Dispose();
    }

    /// <summary>ConPTY sets the title to the shell's full path ("C:\...\pwsh.exe"); a tab shows just "pwsh".</summary>
    static string TabTitle(string title) =>
        title.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(title) ? Path.GetFileNameWithoutExtension(title) : title;

    /// <summary>Phase 2 mock lines as VT text, so mock and live terminals share one renderer.</summary>
    static string MockToVt(IReadOnlyList<TermLine> lines)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append("\r\n");
            foreach (var span in lines[i].Spans)
            {
                sb.Append(span.Color switch
                {
                    TermColor.Dim => "\x1b[90m",
                    TermColor.Green => "\x1b[32m",
                    TermColor.Blue => "\x1b[34m",
                    TermColor.Yellow => "\x1b[33m",
                    TermColor.Red => "\x1b[31m",
                    TermColor.Magenta => "\x1b[35m",
                    TermColor.Cyan => "\x1b[36m",
                    _ => "\x1b[39m",
                });
                sb.Append(span.Text);
            }
            sb.Append("\x1b[0m");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Shell integration: the shells report their working directory with OSC 7 after every prompt, so the sidebar and
/// the file explorer follow <c>cd</c>. The user's own prompt is kept and wrapped.
/// </summary>
public static class ShellIntegration
{
    public static string CommandLine(ShellKind kind)
    {
        var shell = Shells.CommandLine(kind);
        if (kind != ShellKind.PowerShell) return shell;
        // Runs after the profile, wrapping whatever prompt it defined. [char]27 works in Windows PowerShell 5.1 too.
        const string script =
            "$global:__armuxPrompt = $function:prompt; " +
            "function global:prompt { $l = $executionContext.SessionState.Path.CurrentLocation; " +
            "if ($l.Provider.Name -eq 'FileSystem') { [Console]::Write([char]27 + ']7;file://localhost/' + ($l.ProviderPath -replace '\\\\', '/') + [char]7) }; " +
            "& $global:__armuxPrompt }";
        return $"{shell} -NoExit -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}";
    }

    public static IReadOnlyDictionary<string, string> Environment(ShellKind kind, IReadOnlyDictionary<string, string> extra)
    {
        var env = new Dictionary<string, string>(extra)
        {
            ["TERM_PROGRAM"] = "armux",
            ["COLORTERM"] = "truecolor",
        };
        if (kind == ShellKind.Cmd)
        {
            // cmd has no prompt hook; PROMPT codes can still emit OSC 7 with the raw path ($P).
            var prompt = System.Environment.GetEnvironmentVariable("PROMPT") is { Length: > 0 } p ? p : "$P$G";
            env["PROMPT"] = "$E]7;file://localhost/$P$E\\" + prompt;
        }
        return env;
    }
}
