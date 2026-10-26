using System.Diagnostics;
using Armux.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using VirtualKey = Windows.System.VirtualKey;

namespace Armux.App;

public interface IExplorerHost
{
    /// <summary>Opens a file as a read-only tab in the focused pane, or in a new pane split to the right.</summary>
    void OpenFile(string path, bool newPane);
    void CloseExplorer();
}

/// <summary>
/// File explorer (Warp's file tree, deviation D-13): the current workspace's folder as a lazily expanded tree.
/// Folders on screen are watched, so the tree follows changes on disk. Created on first show and reused:
/// <see cref="Suspend"/> drops its rows, listings and watchers while hidden.
/// </summary>
public sealed partial class ExplorerPanel : Grid
{
    const double RowHeight = 24, Indent = 14;
    /// <summary>Drag data property holding the full path of an entry dragged out of the explorer.</summary>
    public const string DragPathKey = "armux.path";

    readonly IExplorerHost host;
    readonly StackPanel list = new() { Padding = new Thickness(0, 2, 0, 12) };
    readonly ScrollViewer scroller;
    readonly TextBlock name, path;
    readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> changed = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherQueueTimer debounce;
    FileTree tree;
    List<FileRow> rows = [];
    string? selectedPath;
    bool suspended;

    public FileTree Tree => tree;

    public ExplorerPanel(FileTree tree, string cwd, bool exact, IExplorerHost host)
    {
        this.tree = tree;
        this.host = host;
        Background = Theme.Brush("Sidebar");
        BorderBrush = Theme.Brush("Divider");
        BorderThickness = new Thickness(1, 0, 0, 0);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());

        var head = new Grid { Padding = new Thickness(12, 6, 6, 6) };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        name = Ui.Text("", 12.5, Theme.Brush("Text"), semibold: true);
        path = Ui.Text("", 11, Theme.Brush("TextFaint"));
        titles.Children.Add(name);
        titles.Children.Add(path);
        head.Children.Add(titles);
        var dim = Theme.Brush("TextDim");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Ui.IconButton(Ui.Glyph(Ui.Add, 12, dim), "New file", () => _ = NewFileAsync(TargetFolder()), 24));
        buttons.Children.Add(Ui.IconButton(Ui.Glyph(Ui.ChevronUp, 11, dim), "Collapse all", () => { this.tree.CollapseAll(); Render(); }, 24));
        buttons.Children.Add(Ui.IconButton(Ui.Glyph(Ui.Close, 10, dim), "Close explorer (Ctrl+Shift+E)", host.CloseExplorer, 24));
        SetColumn(buttons, 1);
        head.Children.Add(buttons);
        Children.Add(head);

        scroller = new ScrollViewer { Content = list, IsTabStop = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroller.PreviewKeyDown += OnKey;
        scroller.RightTapped += (_, e) => ShowMenu(new FileEntry(this.tree.Root, name.Text, true), scroller, e);
        SetRow(scroller, 1);
        Children.Add(scroller);

        debounce = DispatcherQueue.GetForCurrentThread().CreateTimer();
        debounce.Interval = TimeSpan.FromMilliseconds(150);
        debounce.IsRepeating = false;
        debounce.Tick += (_, _) => ApplyChanges();

        SetRoot(tree, cwd, exact);
    }

    /// <summary>Shows another root (the explorer follows the current workspace).</summary>
    public void SetRoot(FileTree tree, string cwd, bool exact)
    {
        if (!ReferenceEquals(this.tree, tree)) this.tree.InvalidateAll();   // keep only the shown tree's listings
        this.tree = tree;
        name.Text = Path.GetFileName(tree.Root) is { Length: > 0 } n ? n : tree.Root;
        path.Text = exact ? Shorten(tree.Root) : $"{cwd} not found · showing {Shorten(tree.Root)}";
        ToolTipService.SetToolTip(path, tree.Root);
        selectedPath = null;
        Render();
        scroller.ChangeView(null, 0, null, true);
    }

    static string Shorten(string p)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return p.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + p[home.Length..] : p;
    }

    // ---- rendering ---------------------------------------------------------------------------

    void Render()
    {
        if (suspended) return;
        rows = tree.Rows();
        list.Children.Clear();
        for (int i = 0; i < rows.Count; i++) list.Children.Add(Row(rows[i], i));
        Watch();
    }

    FrameworkElement Row(FileRow r, int index)
    {
        var g = new Grid { Height = RowHeight, Padding = new Thickness(8 + r.Depth * Indent, 0, 8, 0), ColumnSpacing = 4, Margin = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        g.ColumnDefinitions.Add(new ColumnDefinition());

        if (r.Entry is not { } e)
        {
            var more = Ui.Text($"{r.More} more · open in File Explorer", 12, Theme.Brush("TextFaint"));
            more.VerticalAlignment = VerticalAlignment.Center;
            SetColumn(more, 2);
            g.Children.Add(more);
            g.Tapped += (_, _) => Reveal(FolderOfMoreRow(index));
            return g;
        }

        bool open = e.IsDirectory && tree.IsExpanded(e.Path);
        if (e.IsDirectory)
        {
            var chevron = Ui.Glyph(open ? Ui.ChevronDown : Ui.ChevronRight, 8, Theme.Brush("TextFaint"));
            chevron.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(chevron);
        }
        var (glyph, color) = IconFor(e, open);
        var icon = Ui.Glyph(glyph, 12, color);
        icon.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(icon, 1);
        g.Children.Add(icon);
        var label = Ui.Text(e.Name, 12.5, Theme.Brush(e.Name.StartsWith('.') ? "TextDim" : "Text"));
        label.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(label, 2);
        g.Children.Add(label);
        ToolTipService.SetToolTip(g, e.Path);

        bool hover = false;
        void Paint() => g.Background = e.Path == selectedPath ? Theme.Brush("SelectedRow") : hover ? Theme.Brush("SidebarHover") : null;
        Paint();
        g.Tag = (Action)Paint;
        g.PointerEntered += (_, _) => { hover = true; Paint(); };
        g.PointerExited += (_, _) => { hover = false; Paint(); };
        g.Tapped += (_, _) => { Select(e.Path); Activate(e); };
        g.RightTapped += (_, a) => { Select(e.Path, focus: false); ShowMenu(e, g, a); };
        // Drag onto a terminal to type the path (relative to that terminal's directory).
        g.CanDrag = true;
        g.DragStarting += (_, a) =>
        {
            a.Data.SetText(e.Path);
            a.Data.Properties[DragPathKey] = e.Path;
            a.Data.RequestedOperation = DataPackageOperation.Copy;
        };
        return g;
    }

    /// <summary>The folder a "N more" row belongs to: the parent of the entries above it at the same depth.</summary>
    string FolderOfMoreRow(int index)
    {
        for (int j = index - 1; j >= 0; j--)
            if (rows[j].Entry is { } e && rows[j].Depth == rows[index].Depth) return Path.GetDirectoryName(e.Path) ?? tree.Root;
        return tree.Root;
    }

    /// <summary>Folder icons, and per-type icons for common files (Warp shows icons for common file types).</summary>
    static (string Glyph, Brush Color) IconFor(FileEntry e, bool open)
    {
        if (e.IsDirectory) return (open ? Ui.FolderOpen : Ui.Folder, Theme.Hex("#D9A441"));
        string ext = Path.GetExtension(e.Name).ToLowerInvariant();
        (string, string?) v = ext switch
        {
            ".cs" or ".csx" => (Ui.Code, "#A179DC"),
            ".ts" or ".tsx" or ".py" => (Ui.Code, "#3B82F6"),
            ".js" or ".jsx" or ".mjs" or ".cjs" => (Ui.Code, "#EAB308"),
            ".rs" => (Ui.Code, "#F97316"),
            ".go" => (Ui.Code, "#06B6D4"),
            ".c" or ".cpp" or ".cc" or ".h" or ".hpp" => (Ui.Code, "#6366F1"),
            ".java" or ".kt" => (Ui.Code, "#EF4444"),
            ".html" or ".css" or ".scss" or ".vue" or ".svelte" => (Ui.Code, "#EC4899"),
            ".ps1" or ".psm1" or ".sh" or ".bash" or ".bat" or ".cmd" => (Ui.Code, "#14B8A6"),
            ".md" or ".txt" or ".rst" => (Ui.Document, "#60A5FA"),
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".ico" or ".webp" or ".bmp" => (Ui.Photo, "#22C55E"),
            ".json" or ".yml" or ".yaml" or ".toml" or ".xml" or ".csproj" or ".slnx" or ".sln" or ".props"
                or ".targets" or ".config" or ".ini" or ".lock" or ".manifest" => (Ui.Gear, null),
            _ when e.Name.StartsWith('.') => (Ui.Gear, null),
            _ => (Ui.Page, null),
        };
        return (v.Item1, v.Item2 is { } hex ? Theme.Hex(hex) : Theme.Brush("TextFaint"));
    }

    void Select(string? p, bool focus = true)
    {
        selectedPath = p;
        foreach (var c in list.Children)
            if (c is FrameworkElement { Tag: Action paint }) paint();
        if (focus) scroller.Focus(FocusState.Programmatic);
    }

    /// <summary>Click or Enter: folders expand or collapse, files open in a new tab.</summary>
    void Activate(FileEntry e)
    {
        if (e.IsDirectory) { tree.Toggle(e.Path); Render(); }
        else host.OpenFile(e.Path, newPane: false);
    }

    // ---- keyboard ----------------------------------------------------------------------------

    void OnKey(object sender, KeyRoutedEventArgs e)
    {
        int i = rows.FindIndex(r => r.Entry?.Path == selectedPath);
        FileEntry? cur = i >= 0 ? rows[i].Entry : null;
        switch (e.Key)
        {
            case VirtualKey.Down: Move(i + 1); break;
            case VirtualKey.Up: Move(i < 0 ? 0 : i - 1); break;
            case VirtualKey.Right when cur is { IsDirectory: true }:
                if (!tree.IsExpanded(cur.Path)) { tree.Expand(cur.Path); Render(); } else Move(i + 1);
                break;
            case VirtualKey.Left when cur is not null:
                if (cur.IsDirectory && tree.IsExpanded(cur.Path)) { tree.Collapse(cur.Path); Render(); }
                else if (Path.GetDirectoryName(cur.Path) is { } parent && !parent.Equals(tree.Root, StringComparison.OrdinalIgnoreCase)) Select(parent);
                break;
            case VirtualKey.Enter when cur is not null: Activate(cur); break;
            default: return;
        }
        e.Handled = true;
    }

    void Move(int i)
    {
        i = Math.Clamp(i, 0, rows.Count - 1);
        while (i < rows.Count - 1 && rows[i].Entry is null) i++;
        if (i < 0 || rows[i].Entry is not { } e) return;
        Select(e.Path);
        if (list.Children[i] is FrameworkElement el) el.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
    }

    // ---- context menu ------------------------------------------------------------------------

    void ShowMenu(FileEntry e, FrameworkElement anchor, RightTappedRoutedEventArgs args)
    {
        args.Handled = true;
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = true, MenuFlyoutPresenterStyle = Ui.SolidPresenter(typeof(MenuFlyoutPresenter)) };
        MenuFlyoutItem Item(string text, Action run, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
            return item;
        }
        void Sep() => menu.Items.Add(new MenuFlyoutSeparator());

        if (e.IsDirectory)
        {
            Item("New file…", () => _ = NewFileAsync(e.Path));
            Sep();
        }
        else
        {
            Item("Open in new tab", () => host.OpenFile(e.Path, newPane: false));
            Item("Open in new pane", () => host.OpenFile(e.Path, newPane: true));
            Sep();
        }
        Item("Copy path", () => Copy(e.Path));
        Item("Copy relative path", () => Copy(Path.GetRelativePath(tree.Root, e.Path)));
        Sep();
        Item("Reveal in File Explorer", () => Reveal(e.Path));
        if (!e.IsDirectory) Item("Open with default app", () => FileView.OpenExternally(e.Path));
        Sep();
        // Warp's "Attach as context" inserts the file into the agent prompt; armux's equivalent needs a real terminal.
        Item("Insert path in terminal (Phase 3)", () => { }, enabled: false);
        menu.ShowAt(anchor, args.GetPosition(anchor));
    }

    static void Copy(string text)
    {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
    }

    static void Reveal(string p)
    {
        try { Process.Start("explorer.exe", Directory.Exists(p) ? $"\"{p}\"" : $"/select,\"{p}\""); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception) { App.Log(nameof(Reveal), ex); }
    }

    /// <summary>The selected folder, the folder of the selected file, or the root.</summary>
    string TargetFolder() =>
        selectedPath is null ? tree.Root
        : Directory.Exists(selectedPath) ? selectedPath
        : Path.GetDirectoryName(selectedPath) ?? tree.Root;

    async Task NewFileAsync(string dir)
    {
        var box = new TextBox { PlaceholderText = "name.ext" };
        var error = Ui.Text("", 12, Theme.Brush("Term.Red"));
        var body = new StackPanel { Spacing = 6, Width = 360 };
        body.Children.Add(Ui.Text($"In {Shorten(dir)}", 12, Theme.Brush("TextDim")));
        body.Children.Add(box);
        body.Children.Add(error);
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = Theme.Dark ? ElementTheme.Dark : ElementTheme.Light,
            Title = "New file",
            Content = body,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        string? created = null;
        dlg.PrimaryButtonClick += (_, a) =>
        {
            var n = box.Text.Trim();
            if (n.Length == 0 || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { error.Text = "Enter a file name without \\ / : * ? \" < > |"; a.Cancel = true; return; }
            var full = Path.Combine(dir, n);
            try { using (File.Open(full, FileMode.CreateNew)) { } created = full; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error.Text = ex.Message; a.Cancel = true; }
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary || created is null) return;
        if (!dir.Equals(tree.Root, StringComparison.OrdinalIgnoreCase)) tree.Expand(dir);
        tree.Invalidate(dir);
        Render();
        Select(created);
        host.OpenFile(created, newPane: false);
    }

    // ---- watching ----------------------------------------------------------------------------

    /// <summary>One non-recursive watcher per folder on screen: the root and each expanded folder.</summary>
    void Watch()
    {
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tree.Root };
        foreach (var r in rows)
            if (r.Entry is { IsDirectory: true } e && tree.IsExpanded(e.Path)) shown.Add(e.Path);
        foreach (var dir in watchers.Keys.Where(d => !shown.Contains(d)).ToList())
        {
            watchers[dir].Dispose();
            watchers.Remove(dir);
        }
        foreach (var dir in shown.Where(d => !watchers.ContainsKey(d)))
        {
            try
            {
                var w = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                w.Created += (_, a) => Changed(dir);
                w.Deleted += (_, a) => Changed(dir);
                w.Renamed += (_, a) => Changed(dir);
                w.Error += (_, _) => Changed(dir);
                w.EnableRaisingEvents = true;
                watchers[dir] = w;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Called on a thread-pool thread; batches events and re-renders on the UI thread.</summary>
    void Changed(string dir)
    {
        lock (changed) changed.Add(dir);
        DispatcherQueue.TryEnqueue(() => { if (!suspended) { debounce.Stop(); debounce.Start(); } });
    }

    void ApplyChanges()
    {
        string[] dirs;
        lock (changed) { dirs = [.. changed]; changed.Clear(); }
        foreach (var d in dirs) tree.Invalidate(d);
        if (dirs.Length > 0) Render();
    }

    /// <summary>Hidden: stop watching and release the rows and cached listings. Expanded folders are kept.</summary>
    public void Suspend()
    {
        if (suspended) return;
        suspended = true;
        debounce.Stop();
        foreach (var w in watchers.Values) w.Dispose();
        watchers.Clear();
        lock (changed) changed.Clear();
        rows = [];
        list.Children.Clear();
        tree.InvalidateAll();
    }

    /// <summary>Shown again: re-read the folders on screen and watch them.</summary>
    public void Resume()
    {
        if (!suspended) return;
        suspended = false;
        Render();
    }
}
