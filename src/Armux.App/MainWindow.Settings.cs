using Armux.Core;

namespace Armux.App;

/// <summary>Applying settings: every change goes through <see cref="ChangeSettings"/>, which saves and redraws what it affects.</summary>
public sealed partial class MainWindow
{
    public static string SettingsPath =>
        Environment.GetEnvironmentVariable("ARMUX_SETTINGS_FILE") is { Length: > 0 } p ? p
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "armux", "settings.json");

    /// <summary>
    /// Applies a change to the settings, saves them, and updates only what the change touches: theme and interface
    /// font redraw the window, terminal options rebuild terminal views (shells keep running), and so on.
    /// </summary>
    void ChangeSettings(Func<Settings, Settings> change)
    {
        var old = Theme.Settings;
        var next = change(old).Normalized();
        if (next.Equals(old)) return;
        Theme.UseSettings(next);
        try { Theme.Settings.Save(SettingsPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { App.Log(nameof(ChangeSettings), e); }

        bool window = next.Theme != old.Theme || next.UiFont != old.UiFont;
        bool terminals = next.TerminalFont != old.TerminalFont || next.TerminalFontSize != old.TerminalFontSize
            || next.LineHeight != old.LineHeight || next.Ligatures != old.Ligatures
            || next.CursorStyle != old.CursorStyle || next.CursorBlink != old.CursorBlink;
        if (window)
        {
            try { Microsoft.UI.Xaml.Application.Current.Resources["ContentControlThemeFontFamily"] = new Microsoft.UI.Xaml.Media.FontFamily(Theme.UiFont); }
            catch (Exception e) { App.Log("ContentControlThemeFontFamily", e); }
            palette.UpdateFont();
            SetTheme(Theme.Resolve(next.Theme));
        }
        else if (terminals) RefreshTerminalViews();
        if (next.Zoom != old.Zoom) ApplyZoom(new Windows.Foundation.Size(zoomHost.ActualWidth, zoomHost.ActualHeight));
        if (next.WorkspaceIndicator != old.WorkspaceIndicator) state.Indicator = Indicator(next.WorkspaceIndicator);
        if (!next.Keybindings.OrderBy(k => k.Key).SequenceEqual(old.Keybindings.OrderBy(k => k.Key))) RebuildKeymap();
        if (next.TaskbarStatus != old.TaskbarStatus) UpdateTaskbar();
        if (window && settingsPage is not null) ShowSettingsPage(settingsSection);
    }

    static IndicatorStyle Indicator(string setting) => setting switch
    {
        "underline" => IndicatorStyle.Underline,
        "none" => IndicatorStyle.None,
        _ => IndicatorStyle.Accent,
    };

    /// <summary>Rebuilds terminal and file views with the current terminal settings; the shells keep running.</summary>
    void RefreshTerminalViews()
    {
        foreach (var (surface, panel) in panels.ToList())
            if (panel is TerminalView or FileView) panels.Remove(surface);
        if (state.Current is { } ws)
            foreach (var pane in ws.Panes) ViewOf(pane).Refresh();
        FocusKeys();
    }

    /// <summary>Ctrl+Shift+= / - / 0: the terminal font size alone.</summary>
    void ChangeFontSize(double delta) =>
        ChangeSettings(s => s.WithTerminalFontSize(delta == 0 ? Settings.DefaultTerminalFontSize : s.TerminalFontSize + delta));

    static string ExpandHome(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path == "~" ? home : path.StartsWith("~\\") || path.StartsWith("~/") ? Path.Join(home, path[2..]) : path;
    }

    /// <summary>The directory new workspaces start in, as shown in the sidebar ("~" for the user folder).</summary>
    public static string StartDirectory()
    {
        var dir = Theme.Settings.StartDirectory;
        if (dir.Length == 0 || !Directory.Exists(ExpandHome(dir))) return "~";
        var full = Path.GetFullPath(ExpandHome(dir));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return full.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + full[home.Length..] : full;
    }
}
