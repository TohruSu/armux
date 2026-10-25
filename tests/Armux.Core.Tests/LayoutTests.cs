using Armux.Core;

namespace Armux.Core.Tests;

[TestClass]
public sealed class LayoutTests
{
    static (Workspace ws, Pane a) One()
    {
        var a = new Pane();
        a.Surfaces.Add(MockData.Terminal("a"));
        return (new Workspace { Root = a, Focused = a }, a);
    }

    [TestMethod]
    public void SplitRightPutsNewPaneOnTheRightAndFocusesIt()
    {
        var (ws, a) = One();
        var b = Layout.Split(ws, a, SplitDirection.Right, MockData.Terminal("b"));
        Assert.AreSame(b, ws.Focused);
        var r = Layout.Rects(ws.Root);
        Assert.AreEqual(0.5, r[b].X, 1e-9);
        Assert.AreEqual(0.5, r[a].W, 1e-9);
    }

    [TestMethod]
    public void NeighborCrossesNestedSplits()
    {
        // [a | b/c]  : from c going left reaches a, from a going right reaches b (top) or c.
        var (ws, a) = One();
        var b = Layout.Split(ws, a, SplitDirection.Right, MockData.Terminal("b"));
        var c = Layout.Split(ws, b, SplitDirection.Down, MockData.Terminal("c"));
        Assert.AreSame(a, Layout.Neighbor(ws.Root, c, Direction.Left));
        Assert.AreSame(b, Layout.Neighbor(ws.Root, c, Direction.Up));
        Assert.IsNull(Layout.Neighbor(ws.Root, c, Direction.Down));
        Assert.IsNotNull(Layout.Neighbor(ws.Root, a, Direction.Right));
    }

    [TestMethod]
    public void CloseCollapsesParentAndMovesFocusToSibling()
    {
        var (ws, a) = One();
        var b = Layout.Split(ws, a, SplitDirection.Right, MockData.Terminal("b"));
        Assert.IsTrue(Layout.Close(ws, b));
        Assert.AreSame(a, ws.Root);
        Assert.AreSame(a, ws.Focused);
        Assert.IsNull(a.Parent);
        Assert.IsFalse(Layout.Close(ws, a), "last pane cannot close");
    }

    [TestMethod]
    public void EqualizeGivesEqualWidthToThreeColumns()
    {
        var (ws, a) = One();
        var b = Layout.Split(ws, a, SplitDirection.Right, MockData.Terminal("b"));
        Layout.Split(ws, b, SplitDirection.Right, MockData.Terminal("c"));
        Layout.Equalize(ws.Root);
        foreach (var r in Layout.Rects(ws.Root).Values) Assert.AreEqual(1.0 / 3, r.W, 1e-9);
    }

    [TestMethod]
    public void ResizeMovesDividerAndClamps()
    {
        var (ws, a) = One();
        Layout.Split(ws, a, SplitDirection.Right, MockData.Terminal("b"));
        var split = (SplitNode)ws.Root;
        Assert.IsTrue(Layout.Resize(a, Direction.Right, 0.1));
        Assert.AreEqual(0.6, split.Ratio, 1e-9);
        for (int i = 0; i < 20; i++) Layout.Resize(a, Direction.Right, 0.1);
        Assert.AreEqual(0.9, split.Ratio, 1e-9);
        Assert.IsFalse(Layout.Resize(a, Direction.Down), "no horizontal divider to move");
    }

    [TestMethod]
    public void PanesScenarioBuildsRequestedCount()
    {
        for (int n = 1; n <= 6; n++) Assert.AreEqual(n, MockData.Panes(n).Workspaces[0].Panes.Count());
    }
}
