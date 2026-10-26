using System.Collections.ObjectModel;

namespace Armux.Core;

/// <summary>All workspaces and notifications for one window.</summary>
public sealed class AppState : Observable
{
    public ObservableCollection<Workspace> Workspaces { get; } = [];
    public ObservableCollection<Notification> Notifications { get; } = [];

    Workspace? current;
    public Workspace? Current
    {
        get => current;
        set
        {
            if (ReferenceEquals(current, value)) return;
            if (current is not null) current.Selected = false;
            current = value;
            Raise();
            if (value is null) return;
            value.Selected = true;
            // Looking at a surface reads it, as in cmux.
            if (value.Focused?.Selected is { } s) MarkRead(value, s);
        }
    }

    public int UnreadTotal => Notifications.Count(n => !n.Read);

    IndicatorStyle indicator = IndicatorStyle.Accent;
    public IndicatorStyle Indicator { get => indicator; set => Set(ref indicator, value); }

    int nextNotification;

    /// <summary>
    /// Records a notification from a surface (OSC 9/99/777 or the CLI in Phase 3; mock in Phase 2).
    /// Lights the surface, its pane ring and the workspace badge unless the user is already looking at it.
    /// </summary>
    bool windowActive = true;

    /// <summary>
    /// False while the armux window is in the background: nothing is "visible" then, so a notification from the
    /// focused tab stays unread (taskbar and toast) until the user comes back, which reads it.
    /// </summary>
    public bool WindowActive
    {
        get => windowActive;
        set
        {
            if (windowActive == value) return;
            windowActive = value;
            if (value && Current is { Focused.Selected: { } s } ws) MarkRead(ws, s);
        }
    }

    public Notification Notify(Workspace ws, Pane pane, Surface surface, string title, string body)
    {
        var n = new Notification(++nextNotification, ws, pane, surface, title, body, DateTime.Now);
        Notifications.Insert(0, n);
        ws.Status = body;
        bool visible = WindowActive && ReferenceEquals(ws, Current) && ReferenceEquals(ws.Focused, pane) && ReferenceEquals(pane.Selected, surface);
        if (visible) n.Read = true;
        else surface.Unread = true;
        Recount(ws);
        Raise(nameof(UnreadTotal));
        return n;
    }

    /// <summary>Marks a surface read, which clears its notifications, dot and ring.</summary>
    public void MarkRead(Workspace ws, Surface surface)
    {
        surface.Unread = false;
        foreach (var n in Notifications.Where(n => ReferenceEquals(n.Surface, surface))) n.Read = true;
        Recount(ws);
        Raise(nameof(UnreadTotal));
    }

    /// <summary>cmux "toggle unread": flips the surface between read and unread.</summary>
    public void ToggleUnread(Workspace ws, Surface surface)
    {
        if (surface.Unread)
        {
            MarkRead(ws, surface);
            return;
        }
        if (Notifications.FirstOrDefault(n => ReferenceEquals(n.Surface, surface)) is { } last) last.Read = false;
        surface.Unread = true;
        Recount(ws);
        Raise(nameof(UnreadTotal));
    }

    public void MarkAllRead()
    {
        foreach (var n in Notifications) n.Read = true;
        foreach (var ws in Workspaces)
        {
            foreach (var s in ws.Panes.SelectMany(p => p.Surfaces)) s.Unread = false;
            Recount(ws);
        }
        Raise(nameof(UnreadTotal));
    }

    public void Clear()
    {
        MarkAllRead();
        Notifications.Clear();
        Raise(nameof(UnreadTotal));
    }

    /// <summary>Latest unread notification (cmux ⌘⇧U), or null.</summary>
    public Notification? LatestUnread() => Notifications.FirstOrDefault(n => !n.Read);

    void Recount(Workspace ws) =>
        ws.UnreadCount = Notifications.Count(n => !n.Read && ReferenceEquals(n.Workspace, ws));

    /// <summary>Pinned workspaces first, otherwise insertion order.</summary>
    public void SortPinned()
    {
        var ordered = Workspaces.OrderByDescending(w => w.Pinned).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            int at = Workspaces.IndexOf(ordered[i]);
            if (at != i) Workspaces.Move(at, i);
        }
    }
}
