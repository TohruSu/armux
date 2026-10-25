using Armux.Core;
using Layout = Armux.Core.Layout;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Armux.App;

public sealed partial class MainWindow
{
    readonly List<Command> commands = [];
    readonly Dictionary<Chord, Action> keymap = [];
    int simulated;

    /// <summary>Adds a palette command and binds its shortcut. Shortcuts follow ARCHITECTURE.md section 8.</summary>
    void Cmd(string title, string shortcut, Action run)
    {
        commands.Add(new Command(title, title, shortcut, run));
        if (shortcut.Length > 0) keymap[Chord.Parse(shortcut)] = run;
    }

    /// <summary>Binds a shortcut without a palette entry (numbered jumps).</summary>
    void Key(string shortcut, Action run) => keymap[Chord.Parse(shortcut)] = run;

    Workspace? Ws => state.Current;
    Pane? Focused => state.Current?.Focused;

    void RegisterCommands()
    {
        // Workspaces
        Cmd("New Workspace", "Ctrl+Shift+N", NewWorkspace);
        Cmd("Next Workspace", "Alt+Shift+]", () => CycleWorkspace(1));
        Cmd("Previous Workspace", "Alt+Shift+[", () => CycleWorkspace(-1));
        Cmd("Close Workspace", "Ctrl+Alt+Shift+W", () => { if (Ws is { } w) CloseWorkspace(w); });
        Cmd("Rename Workspace…", "Ctrl+Alt+Shift+R", () => { if (Ws is { } w) _ = RenameAsync(w); });
        Cmd("Pin or Unpin Workspace", "", () => { if (Ws is { } w) { w.Pinned = !w.Pinned; state.SortPinned(); } });
        Cmd("Workspace Color: Custom…", "", () => { if (Ws is { } w) _ = CustomColorAsync(w); });
        Cmd("Workspace Color: None", "", () => { if (Ws is { } w) w.Color = null; });
        foreach (var (name, hex) in WorkspaceColors.Presets)
            Cmd($"Workspace Color: {name}", "", () => { if (Ws is { } w) w.Color = hex; });
        Cmd("Workspace Indicator: Accent (left rail)", "", () => state.Indicator = IndicatorStyle.Accent);
        Cmd("Workspace Indicator: Underline", "", () => state.Indicator = IndicatorStyle.Underline);
        Cmd("Workspace Indicator: None", "", () => state.Indicator = IndicatorStyle.None);
        Cmd("Toggle Sidebar", "Ctrl+Shift+B", ToggleSidebar);
        for (int i = 1; i <= 8; i++) { int n = i; Key($"Ctrl+Shift+{n}", () => JumpWorkspace(n - 1)); }
        Key("Ctrl+Shift+9", () => JumpWorkspace(state.Workspaces.Count - 1));

        // Surfaces
        Cmd("New Terminal Tab", "Ctrl+Shift+T", () => { if (Focused is { } p) NewSurface(p, PanelKind.Terminal); });
        Cmd("New Browser Tab", "", () => { if (Focused is { } p) NewSurface(p, PanelKind.Browser); });
        Cmd("Close Tab", "Ctrl+Shift+W", () => { if (Focused is { Selected: { } s } p) CloseSurface(p, s); });
        Cmd("Next Tab", "Ctrl+Tab", () => CycleSurface(1));
        Cmd("Previous Tab", "Ctrl+Shift+Tab", () => CycleSurface(-1));
        for (int i = 1; i <= 8; i++) { int n = i; Key($"Alt+{n}", () => JumpSurface(n - 1)); }
        Key("Alt+9", () => JumpSurface(int.MaxValue));

        // Splits
        Cmd("Split Right", "Ctrl+Shift+D", () => { if (Focused is { } p) SplitPane(p, SplitDirection.Right); });
        Cmd("Split Down", "Ctrl+Alt+Shift+D", () => { if (Focused is { } p) SplitPane(p, SplitDirection.Down); });
        Cmd("Focus Pane Left", "Ctrl+Alt+Left", () => FocusDir(Direction.Left));
        Cmd("Focus Pane Right", "Ctrl+Alt+Right", () => FocusDir(Direction.Right));
        Cmd("Focus Pane Up", "Ctrl+Alt+Up", () => FocusDir(Direction.Up));
        Cmd("Focus Pane Down", "Ctrl+Alt+Down", () => FocusDir(Direction.Down));
        Cmd("Resize Pane Left", "Alt+Shift+Left", () => ResizeDir(Direction.Left));
        Cmd("Resize Pane Right", "Alt+Shift+Right", () => ResizeDir(Direction.Right));
        Cmd("Resize Pane Up", "Alt+Shift+Up", () => ResizeDir(Direction.Up));
        Cmd("Resize Pane Down", "Alt+Shift+Down", () => ResizeDir(Direction.Down));
        Cmd("Toggle Pane Zoom", "Ctrl+Alt+Shift+Enter", ToggleZoom);
        Cmd("Equalize Splits", "Ctrl+Alt+=", () => { if (Ws is { } w) { Layout.Equalize(w.Root); RenderContent(); } });
        Cmd("Flash Focused Panel", "Ctrl+Alt+Shift+H", () => { if (Focused is { } p) ViewOf(p).Flash(); });
        Cmd("Open Browser in Split", "Ctrl+Alt+Shift+L", () =>
        {
            if (Ws is not { } w || Focused is not { } p) return;
            Layout.Split(w, p, SplitDirection.Right, MockData.Browser());
            RenderContent();
        });

        // Notifications
        Cmd("Show Notifications", "Ctrl+Shift+I", () => bellFlyout.ShowAt(bellButton));
        Cmd("Jump to Latest Unread", "Ctrl+Alt+Shift+U", () => { if (state.LatestUnread() is { } n) JumpTo(n); });
        Cmd("Toggle Unread", "Ctrl+Alt+U", () => { if (Ws is { } w && Focused?.Selected is { } s) state.ToggleUnread(w, s); });
        Cmd("Mark All Notifications Read", "", state.MarkAllRead);
        Cmd("Clear All Notifications", "", state.Clear);
        Cmd("Simulate Agent Notification (mock)", "", SimulateNotification);

        // Terminal and window
        Cmd("Clear Scrollback", "Ctrl+Shift+K", () =>
        {
            if (Focused?.Selected is not { Kind: PanelKind.Terminal } s) return;
            s.Lines.Clear();
            s.Lines.AddRange(MockData.Prompt());
            panels.Remove(s);
            ViewOf(Focused).Refresh();
        });
        Cmd("Theme: Dark", "", () => SetTheme(true));
        Cmd("Theme: Light", "", () => SetTheme(false));
        Cmd("Theme: Match Windows", "", () => SetTheme(new Windows.UI.ViewManagement.UISettings()
            .GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R < 128));
        Cmd("New Window", "Ctrl+Alt+Shift+N", () => new MainWindow(opts with { Scenario = "idle", Palette = false, Notifications = false, Zoom = false, Menu = false, Colors = false, Select = 0 }).Activate());
        Cmd("Command Palette", "Ctrl+Shift+P", OpenPalette);

        root.PreviewKeyDown += OnKey;
    }

    void OpenPalette()
    {
        // Workspace switcher results, like cmux's palette.
        var list = new List<Command>(commands);
        for (int i = 0; i < state.Workspaces.Count; i++)
        {
            var w = state.Workspaces[i];
            list.Add(new Command($"ws:{w.Id}", $"Go to Workspace: {w.Title}", i < 8 ? $"Ctrl+Shift+{i + 1}" : "", () => SelectWorkspace(w)));
        }
        palette.Open(list);
    }

    static bool Down(VirtualKey k) => InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(CoreVirtualKeyStates.Down);

    static string? KeyName(VirtualKey k) => k switch
    {
        >= VirtualKey.A and <= VirtualKey.Z => k.ToString(),
        >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((int)k - (int)VirtualKey.Number0).ToString(),
        (VirtualKey)187 => "=",
        (VirtualKey)189 => "-",
        (VirtualKey)219 => "[",
        (VirtualKey)221 => "]",
        (VirtualKey)188 => ",",
        VirtualKey.Enter => "ENTER",
        VirtualKey.Tab => "TAB",
        VirtualKey.Left => "LEFT",
        VirtualKey.Right => "RIGHT",
        VirtualKey.Up => "UP",
        VirtualKey.Down => "DOWN",
        _ => null,
    };

    void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (palette.IsOpen) return;
        if (KeyName(e.Key) is not { } key) return;
        var chord = new Chord(Down(VirtualKey.Control), Down(VirtualKey.Menu), Down(VirtualKey.Shift), key);
        if (!keymap.TryGetValue(chord, out var run)) return;
        e.Handled = true;
        run();
    }

    // ---- actions -------------------------------------------------------------------------------

    void CycleWorkspace(int delta)
    {
        if (Ws is not { } w || state.Workspaces.Count == 0) return;
        int i = (state.Workspaces.IndexOf(w) + delta + state.Workspaces.Count) % state.Workspaces.Count;
        SelectWorkspace(state.Workspaces[i]);
    }

    void JumpWorkspace(int index)
    {
        if (index >= 0 && index < state.Workspaces.Count) SelectWorkspace(state.Workspaces[index]);
    }

    void CycleSurface(int delta)
    {
        if (Focused is not { Selected: { } s } p || p.Surfaces.Count < 2) return;
        int i = (p.Surfaces.IndexOf(s) + delta + p.Surfaces.Count) % p.Surfaces.Count;
        SelectSurface(p, p.Surfaces[i]);
    }

    void JumpSurface(int index)
    {
        if (Focused is not { } p || p.Surfaces.Count == 0) return;
        SelectSurface(p, p.Surfaces[Math.Min(index, p.Surfaces.Count - 1)]);
    }

    void FocusDir(Direction d)
    {
        if (Ws is not { } w || w.Zoomed is not null || Focused is not { } p) return;
        if (Layout.Neighbor(w.Root, p, d) is { } n) FocusPane(n);
    }

    void ResizeDir(Direction d)
    {
        if (Focused is { } p && Layout.Resize(p, d)) RenderContent();
    }

    void ToggleZoom()
    {
        if (Ws is not { } w || Focused is not { } p) return;
        w.Zoomed = w.Zoomed is null && w.Panes.Count() > 1 ? p : null;
        RenderContent();
    }

    void SetTheme(bool dark)
    {
        Theme.Use(dark);
        foreach (var (s, panel) in panels.ToList())
            if (panel is BrowserPanel bp) { browsers.Close(bp); panels.Remove(s); }
        palette.Background = Theme.Brush("Overlay");
        RenderAll();
    }

    /// <summary>Phase 2 stand-in for OSC 9/99/777 and `armux notify`: notifies a surface the user is not looking at.</summary>
    void SimulateNotification()
    {
        var targets = state.Workspaces
            .SelectMany(w => w.Panes.SelectMany(p => p.Surfaces.Where(s => s.Kind == PanelKind.Terminal).Select(s => (w, p, s))))
            .Where(t => !(ReferenceEquals(t.w, Ws) && ReferenceEquals(t.p, Focused) && ReferenceEquals(t.p.Selected, t.s)))
            .ToList();
        if (targets.Count == 0) return;
        var (ws, pane, surface) = targets[simulated++ % targets.Count];
        string[] bodies = ["Claude is waiting for your input", "Agent turn complete", "Tests finished: 24 passed", "Build failed: 1 error"];
        state.Notify(ws, pane, surface, surface.Title, bodies[simulated % bodies.Length]);
    }
}
