namespace Armux.Core;

/// <summary>Split-tree operations: split, close, directional focus, resize, equalize.</summary>
public static class Layout
{
    public readonly record struct Rect(double X, double Y, double W, double H)
    {
        public double CenterX => X + W / 2;
        public double CenterY => Y + H / 2;
    }

    public static IEnumerable<Pane> Leaves(PaneNode node) => node switch
    {
        Pane p => [p],
        SplitNode s => Leaves(s.First).Concat(Leaves(s.Second)),
        _ => [],
    };

    /// <summary>Splits <paramref name="pane"/> and returns the new pane, which holds <paramref name="surface"/>.</summary>
    public static Pane Split(Workspace ws, Pane pane, SplitDirection dir, Surface surface)
    {
        var added = new Pane();
        added.Surfaces.Add(surface);
        added.Selected = surface;
        var parent = pane.Parent;
        var split = new SplitNode { Direction = dir, First = pane, Second = added };
        Replace(ws, parent, pane, split);
        ws.Focused = added;
        ws.Zoomed = null;
        return added;
    }

    /// <summary>Removes <paramref name="pane"/>. Returns false when it is the last pane in the workspace.</summary>
    public static bool Close(Workspace ws, Pane pane)
    {
        if (pane.Parent is not { } parent) return false;
        var sibling = ReferenceEquals(parent.First, pane) ? parent.Second : parent.First;
        Replace(ws, parent.Parent, parent, sibling);
        if (ReferenceEquals(ws.Focused, pane)) ws.Focused = Leaves(sibling).First();
        if (ReferenceEquals(ws.Zoomed, pane)) ws.Zoomed = null;
        return true;
    }

    static void Replace(Workspace ws, SplitNode? parent, PaneNode old, PaneNode with)
    {
        if (parent is null)
        {
            ws.Root = with;
            with.Parent = null;
        }
        else if (ReferenceEquals(parent.First, old)) parent.First = with;
        else parent.Second = with;
    }

    /// <summary>Lays the tree out in a unit square.</summary>
    public static Dictionary<Pane, Rect> Rects(PaneNode root)
    {
        var result = new Dictionary<Pane, Rect>();
        void Walk(PaneNode n, Rect r)
        {
            switch (n)
            {
                case Pane p: result[p] = r; break;
                case SplitNode s when s.Direction == SplitDirection.Right:
                    Walk(s.First, r with { W = r.W * s.Ratio });
                    Walk(s.Second, r with { X = r.X + r.W * s.Ratio, W = r.W * (1 - s.Ratio) });
                    break;
                case SplitNode s:
                    Walk(s.First, r with { H = r.H * s.Ratio });
                    Walk(s.Second, r with { Y = r.Y + r.H * s.Ratio, H = r.H * (1 - s.Ratio) });
                    break;
            }
        }
        Walk(root, new Rect(0, 0, 1, 1));
        return result;
    }

    /// <summary>Nearest pane in <paramref name="dir"/> from <paramref name="from"/>, or null at the edge.</summary>
    public static Pane? Neighbor(PaneNode root, Pane from, Direction dir)
    {
        const double eps = 1e-9;
        var rects = Rects(root);
        var a = rects[from];
        Pane? best = null;
        double bestScore = double.MaxValue;
        foreach (var (p, b) in rects)
        {
            if (ReferenceEquals(p, from)) continue;
            bool beyond = dir switch
            {
                Direction.Left => b.X + b.W <= a.X + eps,
                Direction.Right => b.X >= a.X + a.W - eps,
                Direction.Up => b.Y + b.H <= a.Y + eps,
                _ => b.Y >= a.Y + a.H - eps,
            };
            if (!beyond) continue;
            bool horizontal = dir is Direction.Left or Direction.Right;
            // Must overlap on the perpendicular axis.
            double overlap = horizontal
                ? Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y)
                : Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X);
            if (overlap <= eps) continue;
            double gap = horizontal ? Math.Abs(b.CenterX - a.CenterX) : Math.Abs(b.CenterY - a.CenterY);
            double drift = horizontal ? Math.Abs(b.CenterY - a.CenterY) : Math.Abs(b.CenterX - a.CenterX);
            double score = gap + drift * 0.1;
            if (score < bestScore) { bestScore = score; best = p; }
        }
        return best;
    }

    /// <summary>Moves the nearest divider of <paramref name="pane"/> that runs across <paramref name="dir"/>.</summary>
    public static bool Resize(Pane pane, Direction dir, double delta = 0.05)
    {
        var wanted = dir is Direction.Left or Direction.Right ? SplitDirection.Right : SplitDirection.Down;
        PaneNode child = pane;
        for (var s = pane.Parent; s is not null; child = s, s = s.Parent)
        {
            if (s.Direction != wanted) continue;
            double sign = dir is Direction.Right or Direction.Down ? 1 : -1;
            // Growing toward the divider: the first child grows when moving right/down.
            s.Ratio = Math.Clamp(s.Ratio + sign * delta, 0.1, 0.9);
            return true;
        }
        return false;
    }

    public static void Equalize(PaneNode node)
    {
        if (node is not SplitNode s) return;
        int a = CountAlong(s.First, s.Direction), b = CountAlong(s.Second, s.Direction);
        s.Ratio = (double)a / (a + b);
        Equalize(s.First);
        Equalize(s.Second);
    }

    static int CountAlong(PaneNode n, SplitDirection dir) => n switch
    {
        SplitNode s when s.Direction == dir => CountAlong(s.First, dir) + CountAlong(s.Second, dir),
        SplitNode s => Math.Max(CountAlong(s.First, dir), CountAlong(s.Second, dir)),
        _ => 1,
    };
}
