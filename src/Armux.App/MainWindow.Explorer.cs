using Armux.Core;
using Layout = Armux.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Armux.App;

/// <summary>The file explorer column: hidden by default, opened beside the sidebar, following the current workspace.</summary>
public sealed partial class MainWindow
{
    const double ExplorerWidth = 260;

    readonly Border explorerHost = new() { Visibility = Visibility.Collapsed };
    /// <summary>One tree per root folder, so expanded folders survive workspace switches and reopening.</summary>
    readonly Dictionary<string, FileTree> trees = new(StringComparer.OrdinalIgnoreCase);
    ExplorerPanel? explorer;
    Button explorerButton = null!;
    bool explorerVisible;

    void ToggleExplorer()
    {
        explorerVisible = !explorerVisible;
        RenderExplorer(rebuild: false);
    }

    public void CloseExplorer()
    {
        if (explorerVisible) ToggleExplorer();
    }

    /// <summary>
    /// Shows, updates or hides the explorer. <paramref name="rebuild"/> recreates it after a theme or font change.
    /// The panel is created on first show and reused; hiding suspends it (no rows, listings or watchers).
    /// </summary>
    void RenderExplorer(bool rebuild)
    {
        explorerButton.Background = explorerVisible ? Theme.Brush("SidebarHover") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (rebuild && explorer is not null)
        {
            explorer.Suspend();
            explorerHost.Child = null;
            explorer = null;
        }
        if (!explorerVisible || state.Current is not { } ws)
        {
            if (explorerHost.Visibility == Visibility.Collapsed) return;
            explorer?.Suspend();
            explorerHost.Visibility = Visibility.Collapsed;
            explorerCol.Width = new GridLength(0);
            // The released rows' native XAML objects are freed only when their managed wrappers are collected, and an
            // idle app may not collect for a long time. Measured: +1.9 MB private per open/close without this.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = FileTree.Resolve(ws.Cwd, home, out bool exact);
        if (!trees.TryGetValue(root, out var tree)) trees[root] = tree = new FileTree(root);
        if (explorer is null)
        {
            explorer = new ExplorerPanel(tree, ws.Cwd, exact, this);
            explorerHost.Child = explorer;
        }
        else
        {
            if (!ReferenceEquals(explorer.Tree, tree)) explorer.SetRoot(tree, ws.Cwd, exact);
            explorer.Resume();
        }
        explorerHost.Visibility = Visibility.Visible;
        explorerCol.Width = new GridLength(ExplorerWidth);
    }

    public void OpenFile(string path, bool newPane)
    {
        if (state.Current is not { } ws || ws.Focused is not { } pane) return;
        if (!newPane && pane.Surfaces.FirstOrDefault(s => s.Kind == PanelKind.File && s.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            SelectSurface(pane, open);
            return;
        }
        var surface = new Surface { Kind = PanelKind.File, Title = Path.GetFileName(path), FilePath = path };
        if (newPane)
        {
            if (ws.Zoomed is not null) ws.Zoomed = null;
            var created = Layout.Split(ws, pane, SplitDirection.Right, surface);
            RenderContent();
            FocusPane(created);
            return;
        }
        pane.Surfaces.Add(surface);
        SelectSurface(pane, surface);
    }
}
