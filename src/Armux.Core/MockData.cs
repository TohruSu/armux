namespace Armux.Core;

/// <summary>Phase 2 mock content. Every string here is invented; none is copied from cmux.</summary>
public static class MockData
{
    static TermLine L(params (string Text, TermColor Color)[] spans) =>
        new(spans.Select(s => new TermSpan(s.Text, s.Color)).ToList());

    static TermLine P(string text) => TermLine.Plain(text);

    public static Surface Terminal(string title, IEnumerable<TermLine>? lines = null) =>
        new() { Kind = PanelKind.Terminal, Title = title, Lines = lines?.ToList() ?? Prompt() };

    public static Surface Browser(string url = "http://localhost:3000/", string title = "armux dev server") =>
        new() { Kind = PanelKind.Browser, Title = title, Url = url, MockPage = true };

    public static List<TermLine> Prompt() =>
    [
        L(("PowerShell 7.6.5", TermColor.Dim)),
        P(""),
        L(("PS ", TermColor.Blue), ("~\\dev\\armux", TermColor.Cyan), ("> ", TermColor.Default)),
    ];

    public static List<TermLine> AgentSession(string task) =>
    [
        L(("PS ", TermColor.Blue), ("~\\dev\\armux", TermColor.Cyan), ("> ", TermColor.Default), ("claude", TermColor.Default)),
        P(""),
        L(("> ", TermColor.Dim), (task, TermColor.Default)),
        P(""),
        L(("● ", TermColor.Green), ("Read", TermColor.Default), ("(src/Armux.App/MainWindow.xaml)", TermColor.Dim)),
        L(("  └ ", TermColor.Dim), ("Read 212 lines", TermColor.Dim)),
        L(("● ", TermColor.Green), ("Bash", TermColor.Default), ("(dotnet build -c Release)", TermColor.Dim)),
        L(("  └ ", TermColor.Dim), ("Build succeeded. 0 Warning(s) 0 Error(s)", TermColor.Dim)),
        L(("● ", TermColor.Green), ("Edit", TermColor.Default), ("(src/Armux.Core/Layout.cs)", TermColor.Dim)),
        L(("  └ ", TermColor.Dim), ("Updated Neighbor() to prefer the pane with the larger overlap", TermColor.Dim)),
        P(""),
        P("The split tree now picks the closest pane in the requested direction."),
        P("Directional focus works across nested splits. Run the tests?"),
        P(""),
        L(("> ", TermColor.Dim), ("█", TermColor.Default)),
        P(""),
        L(("  ? for shortcuts", TermColor.Dim)),
    ];

    public static List<TermLine> DevServer() =>
    [
        L(("PS ", TermColor.Blue), ("~\\dev\\armux\\web", TermColor.Cyan), ("> ", TermColor.Default), ("npm run dev", TermColor.Default)),
        P(""),
        L(("> web@0.1.0 dev", TermColor.Dim)),
        L(("> vite --port 3000", TermColor.Dim)),
        P(""),
        L(("  VITE v6.3.5  ", TermColor.Green), ("ready in 412 ms", TermColor.Dim)),
        P(""),
        L(("  ➜  ", TermColor.Green), ("Local:   ", TermColor.Default), ("http://localhost:3000/", TermColor.Cyan)),
        L(("  ➜  ", TermColor.Green), ("Network: ", TermColor.Default), ("use --host to expose", TermColor.Dim)),
        L(("  ➜  ", TermColor.Dim), ("press h + enter to show help", TermColor.Dim)),
        L(("10:42:07 ", TermColor.Dim), ("[vite] ", TermColor.Cyan), ("page reload ", TermColor.Green), ("src/App.tsx", TermColor.Dim)),
    ];

    public static List<TermLine> Tests() =>
    [
        L(("PS ", TermColor.Blue), ("~\\dev\\armux", TermColor.Cyan), ("> ", TermColor.Default), ("dotnet test", TermColor.Default)),
        L(("  Determining projects to restore...", TermColor.Dim)),
        L(("  Armux.Core.Tests ", TermColor.Default), ("succeeded", TermColor.Green)),
        P(""),
        L(("Test summary: ", TermColor.Default), ("total: 24, failed: 0, succeeded: 24", TermColor.Green), (", skipped: 0, duration: 1.2s", TermColor.Dim)),
        L(("PS ", TermColor.Blue), ("~\\dev\\armux", TermColor.Cyan), ("> ", TermColor.Default)),
    ];

    /// <summary>One workspace with a single terminal pane. Used for the idle memory scenario.</summary>
    public static AppState Idle()
    {
        var state = new AppState();
        var ws = NewWorkspace("pwsh", "", "main", "~\\dev\\armux", MockData.Terminal("pwsh"));
        state.Workspaces.Add(ws);
        state.Current = ws;
        return state;
    }

    /// <summary>One workspace with <paramref name="panes"/> terminal panes in a balanced grid.</summary>
    public static AppState Panes(int panes)
    {
        var state = Idle();
        var ws = state.Workspaces[0];
        for (int i = 1; i < panes; i++)
        {
            var target = ws.Panes.OrderByDescending(p => Area(ws, p)).First();
            var rect = Layout.Rects(ws.Root)[target];
            Layout.Split(ws, target, rect.W >= rect.H ? SplitDirection.Right : SplitDirection.Down, Terminal($"pwsh {i + 1}"));
        }
        Layout.Equalize(ws.Root);
        ws.Focused = ws.Panes.First();
        return state;
    }

    static double Area(Workspace ws, Pane p) { var r = Layout.Rects(ws.Root)[p]; return r.W * r.H; }

    public static Workspace NewWorkspace(string title, string status, string branch, string cwd, Surface first)
    {
        var pane = new Pane();
        pane.Surfaces.Add(first);
        pane.Selected = first;
        return new Workspace { Title = title, Status = status, Branch = branch, Cwd = cwd, Root = pane, Focused = pane };
    }

    /// <summary>The demo layout used for screenshots: six workspaces, splits, a browser and pending notifications.</summary>
    public static AppState Demo()
    {
        var state = new AppState();

        var agent = Terminal("claude: split tree focus", AgentSession("Fix directional focus across nested splits"));
        var main = NewWorkspace("split tree focus", "Agent turn complete", "main*", "~\\dev\\armux", agent);
        main.Color = "#3B82F6";
        main.Pinned = true;
        var mainPane = (Pane)main.Root;
        var right = Layout.Split(main, mainPane, SplitDirection.Right, Terminal("npm run dev", DevServer()));
        Layout.Split(main, right, SplitDirection.Down, Browser());
        mainPane.Surfaces.Add(Terminal("dotnet test", Tests()));
        main.Focused = mainPane;
        main.Ports = ":3000";
        state.Workspaces.Add(main);

        var review = Terminal("codex: review", AgentSession("Review the notification store for race conditions"));
        var rev = NewWorkspace("notification review", "", "feat/notify", "~\\dev\\armux", review);
        rev.Color = "#22C55E";
        rev.Pr = "PR #12 · checks passing";
        var revRight = Layout.Split(rev, (Pane)rev.Root, SplitDirection.Right, Terminal("claude: tests", AgentSession("Add tests for MarkRead and LatestUnread")));
        rev.Focused = (Pane)((SplitNode)rev.Root).First;
        state.Workspaces.Add(rev);

        var docs = NewWorkspace("arm64 setup docs", "Running", "docs/devices", "~\\dev\\armux", Terminal("claude: docs", AgentSession("Document setup on Snapdragon X Elite")));
        docs.Color = "#F59E0B";
        state.Workspaces.Add(docs);

        var ci = NewWorkspace("ci job summary", "", "ci/summary", "~\\dev\\armux", Terminal("gh run watch", Tests()));
        ci.Pr = "PR #14 · 1 check running";
        state.Workspaces.Add(ci);

        var web = NewWorkspace("landing page", "Running", "main", "~\\dev\\armux\\web", Terminal("npm run dev", DevServer()));
        web.Color = "#EC4899";
        web.Ports = ":3000 :5173";
        state.Workspaces.Add(web);

        var wsl = NewWorkspace("wsl: ubuntu", "", "", "~/projects", Terminal("bash", [L(("dev@surface", TermColor.Green), (":", TermColor.Default), ("~/projects", TermColor.Blue), ("$ ", TermColor.Default))]));
        state.Workspaces.Add(wsl);

        state.Current = main;

        // Pending attention in background workspaces and one background pane of the current workspace.
        state.Notify(rev, revRight, revRight.Surfaces[0], "Claude Code", "Claude is waiting for your input: run the new tests?");
        state.Notify(rev, (Pane)((SplitNode)rev.Root).First, review, "Codex", "Review done. 2 findings in AppState.cs");
        state.Notify(ci, (Pane)ci.Root, ci.Panes.First().Surfaces[0], "gh", "Run #31 finished: all checks passed");
        state.Notify(main, right, right.Surfaces[0], "npm run dev", "Build error in src/App.tsx (line 14)");
        return state;
    }
}
