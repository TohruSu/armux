using Armux.Core;

namespace Armux.Core.Tests;

[TestClass]
public sealed class StateTests
{
    [TestMethod]
    public void NotifyInBackgroundLightsSurfaceAndBadge()
    {
        var state = MockData.Idle();
        var bg = MockData.NewWorkspace("bg", "", "main", "~", MockData.Terminal("t"));
        state.Workspaces.Add(bg);
        var pane = bg.Panes.Single();
        state.Notify(bg, pane, pane.Selected!, "Claude Code", "waiting for input");

        Assert.IsTrue(pane.Selected!.Unread);
        Assert.IsTrue(pane.NeedsAttention);
        Assert.AreEqual(1, bg.UnreadCount);
        Assert.AreEqual("waiting for input", bg.Status);
        Assert.AreEqual(1, state.UnreadTotal);
    }

    [TestMethod]
    public void NotifyOnVisibleSurfaceIsReadImmediately()
    {
        var state = MockData.Idle();
        var ws = state.Current!;
        var pane = ws.Focused!;
        state.Notify(ws, pane, pane.Selected!, "t", "done");
        Assert.IsFalse(pane.NeedsAttention);
        Assert.AreEqual(0, state.UnreadTotal);
    }

    [TestMethod]
    public void SwitchingToWorkspaceReadsFocusedSurfaceOnly()
    {
        var state = MockData.Demo();
        var rev = state.Workspaces.Single(w => w.Title == "notification review");
        Assert.AreEqual(2, rev.UnreadCount);
        state.Current = rev;
        Assert.AreEqual(1, rev.UnreadCount, "the other pane still needs attention");
    }

    [TestMethod]
    public void LatestUnreadIsNewestAndMarkAllReadClearsEverything()
    {
        var state = MockData.Demo();
        Assert.AreEqual("npm run dev", state.LatestUnread()!.Title);
        state.MarkAllRead();
        Assert.IsNull(state.LatestUnread());
        Assert.IsTrue(state.Workspaces.All(w => w.UnreadCount == 0));
    }

    [TestMethod]
    public void PinnedWorkspacesSortFirst()
    {
        var state = MockData.Demo();
        var last = state.Workspaces[^1];
        last.Pinned = true;
        state.SortPinned();
        Assert.IsTrue(state.Workspaces.TakeWhile(w => w.Pinned).Contains(last));
        Assert.IsFalse(state.Workspaces.SkipWhile(w => w.Pinned).Any(w => w.Pinned));
    }

    [TestMethod]
    public void PaletteSearchRanksWordStartsFirst()
    {
        var cmds = new[] { "Split Right", "Split Down", "Toggle Sidebar", "Show Notifications" }
            .Select(t => new Command(t, t, "", () => { })).ToList();
        Assert.AreEqual("Split Right", CommandSearch.Filter(cmds, "spr").First().Title);
        Assert.AreEqual("Show Notifications", CommandSearch.Filter(cmds, "notif").First().Title);
        Assert.AreEqual(0, CommandSearch.Filter(cmds, "xyz").Count());
    }

    [TestMethod]
    public void PaletteSearchPrefersTitlePrefix()
    {
        var cmds = new[] { "Equalize Splits", "Split Down", "Split Right", "Open Browser in Split" }
            .Select(t => new Command(t, t, "", () => { })).ToList();
        var top2 = CommandSearch.Filter(cmds, "split").Take(2).Select(c => c.Title).ToList();
        CollectionAssert.AreEquivalent(new[] { "Split Down", "Split Right" }, top2);
    }

    [TestMethod]
    public void ChordParsesModifiersAndPlusKey()
    {
        var c = Chord.Parse("Ctrl+Alt+Shift+D");
        Assert.IsTrue(c.Ctrl && c.Alt && c.Shift);
        Assert.AreEqual("D", c.Key);
        Assert.AreEqual("Ctrl+Alt+Shift+D", c.ToString());
        Assert.AreEqual("=", Chord.Parse("Ctrl+=").Key);
    }
}
