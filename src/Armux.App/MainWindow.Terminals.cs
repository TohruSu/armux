using Armux.Core;
using Armux.Core.Pty;
using Armux.Core.Vt;
using Layout = Armux.Core.Layout;
using Microsoft.UI.Xaml;

namespace Armux.App;

/// <summary>Terminal sessions: one per terminal surface, started when its view first gets a size.</summary>
public sealed partial class MainWindow
{
    readonly Dictionary<Surface, TerminalSession> sessions = [];

    /// <summary>A new live terminal surface running the default shell from Settings.</summary>
    static Surface LiveTerminal(string? shell = null)
    {
        var kind = Shells.Parse(shell ?? Theme.Settings.Shell);
        return new Surface { Kind = PanelKind.Terminal, Title = Shells.Name(kind), Shell = Shells.Name(kind) };
    }

    (Workspace Ws, Pane Pane)? Locate(Surface surface)
    {
        foreach (var ws in state.Workspaces)
            foreach (var pane in ws.Panes)
                if (pane.Surfaces.Contains(surface)) return (ws, pane);
        return null;
    }

    TerminalSession SessionFor(Surface surface)
    {
        if (sessions.TryGetValue(surface, out var s)) return s;
        var ws = Locate(surface)?.Ws;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string cwd = FileTree.Resolve(ws?.Cwd ?? "~", home, out _);
        var env = new Dictionary<string, string>
        {
            ["ARMUX_SURFACE_ID"] = surface.Id.ToString(),
            ["ARMUX_WORKSPACE_ID"] = (ws?.Id ?? 0).ToString(),
            ["ARMUX_PIPE"] = PipeServer.PipeName,
            // armux.exe ships next to ArmuxApp.exe, so `armux notify` works in every armux terminal.
            ["PATH"] = AppContext.BaseDirectory.TrimEnd('\\') + ";" + Environment.GetEnvironmentVariable("PATH"),
        };
        s = new TerminalSession(surface, DispatcherQueue, cwd, env);
        s.Notified += n => OnTerminalNotification(surface, n);
        s.CwdChanged += path => OnTerminalCwd(surface, path);
        s.ProcessExited += _ => OnProcessExited(surface);
        sessions[surface] = s;
        return s;
    }

    void DisposeSession(Surface surface)
    {
        if (sessions.Remove(surface, out var s)) s.Dispose();
    }

    /// <summary>OSC 9 / 99 / 777: the ring, badge and notification list, plus a Windows toast when armux is in the background.</summary>
    void OnTerminalNotification(Surface surface, TermNotification n)
    {
        if (Locate(surface) is not { } at) return;
        string title = n.Title.Length > 0 ? n.Title : surface.Title;
        state.Notify(at.Ws, at.Pane, surface, title, n.Body);
        Toasts.ShowIfInactive(this, surface.Id, at.Ws.Title, title, n.Body);
    }

    /// <summary>OSC 7 from the shell: the workspace directory and git branch follow it, and so does the explorer.</summary>
    void OnTerminalCwd(Surface surface, string path)
    {
        if (Locate(surface) is not { } at) return;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        at.Ws.Cwd = path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + path[home.Length..] : path;
        at.Ws.Branch = Git.Branch(path) ?? "";
        if (ReferenceEquals(at.Ws, state.Current)) RenderExplorer(rebuild: false);
    }

    /// <summary>The terminal view of the focused pane's selected tab, if that tab is a terminal.</summary>
    TerminalView? FocusedTerminal() =>
        Focused?.Selected is { Kind: PanelKind.Terminal } s && panels.TryGetValue(s, out var p) ? p as TerminalView : null;

    /// <summary>A shell exited: its tab closes. When it was the last tab of the window, the window closes, as in other terminals.</summary>
    void OnProcessExited(Surface surface)
    {
        if (Locate(surface) is not { } at) return;
        if (state.Workspaces.Sum(w => w.Panes.Sum(p => p.Surfaces.Count)) == 1) { Close(); return; }
        CloseSurface(at.Pane, surface);
    }

    /// <summary>Clicking a toast: show the window and jump to the surface.</summary>
    void JumpToSurface(int surfaceId)
    {
        foreach (var n in state.Notifications)
            if (n.Surface.Id == surfaceId) { JumpTo(n); break; }
        Activate();
    }

    /// <summary>The control API behind the <c>armux</c> CLI (runs on the UI thread).</summary>
    Response HandleRequest(Request r)
    {
        switch (r.Command)
        {
            case "ping":
                return new Response { Ok = true, Output = "armux " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3) };
            case "notify":
                if (Target(r) is not { } surface) return new Response { Error = $"no surface {r.Surface}" };
                if (r.Title.Length == 0 && r.Body.Length == 0) return new Response { Error = "nothing to send: give a title or a body" };
                OnTerminalNotification(surface, new TermNotification(r.Title, r.Body.Length > 0 ? r.Body : r.Title));
                return new Response { Ok = true };
            case "send":
                if (Target(r) is not { Live: true } live) return new Response { Error = $"surface {r.Surface} is not a live terminal" };
                SessionFor(live).Send(r.Body);
                return new Response { Ok = true };
            case "open":
                if (Target(r) is not { } from || Locate(from) is not { } at) return new Response { Error = $"no surface {r.Surface}" };
                if (BrowserPanel.AddressOrSearch(r.Body) is not { } address) return new Response { Error = "give a URL" };
                Layout.Split(at.Ws, at.Pane, SplitDirection.Right, new Surface { Kind = PanelKind.Browser, Title = address, Url = address });
                if (ReferenceEquals(at.Ws, state.Current)) RenderContent();
                return new Response { Ok = true };
            case "list":
                var sb = new System.Text.StringBuilder();
                foreach (var w in state.Workspaces)
                {
                    sb.Append(ReferenceEquals(w, state.Current) ? "* " : "  ").Append(w.Id).Append("  ").Append(w.Title)
                      .Append("  ").AppendLine(w.Location);
                    foreach (var s in w.Panes.SelectMany(p => p.Surfaces))
                        sb.Append("      ").Append(s.Id).Append("  ").Append(s.Kind.ToString().ToLowerInvariant()).Append("  ").AppendLine(s.Title);
                }
                return new Response { Ok = true, Output = sb.ToString() };
            default:
                return new Response { Error = $"unknown command \"{r.Command}\"" };
        }
    }

    /// <summary>The surface a request names, or the focused one when it names none.</summary>
    Surface? Target(Request r) => r.Surface > 0
        ? state.Workspaces.SelectMany(w => w.Panes).SelectMany(p => p.Surfaces).FirstOrDefault(s => s.Id == r.Surface)
        : Focused?.Selected;

    void DisposeAllSessions()
    {
        foreach (var s in sessions.Values) s.Dispose();
        sessions.Clear();
    }
}
