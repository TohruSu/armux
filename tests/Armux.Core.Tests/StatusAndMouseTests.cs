using Armux.Core.Vt;

namespace Armux.Core.Tests;

[TestClass]
public sealed class StatusAndMouseTests
{
    [TestMethod]
    public void Osc94ProgressIsTrackedAndNotANotification()
    {
        var t = new Terminal(10, 2);
        int changes = 0;
        var notes = new List<TermNotification>();
        t.ProgressChanged += () => changes++;
        t.Notified += notes.Add;
        t.Feed("\x1b]9;4;3\x07");
        Assert.AreEqual(3, t.ProgressState);
        t.Feed("\x1b]9;4;1;40\x1b\\");
        Assert.AreEqual((1, 40), (t.ProgressState, t.ProgressValue));
        t.Feed("\x1b]9;4;0\x07");
        Assert.AreEqual(0, t.ProgressState);
        Assert.AreEqual(3, changes);
        Assert.IsEmpty(notes);
    }

    [TestMethod]
    public void BackgroundAndForegroundQueriesAreAnswered()
    {
        var t = new Terminal(10, 2) { ReportedBackground = 0x262626, ReportedForeground = 0xD4D4D4 };
        var replies = new List<string>();
        t.Reply += replies.Add;
        t.Feed("\x1b]11;?\x1b\\\x1b]10;?\x07");
        CollectionAssert.AreEqual(new[] { "\x1b]11;rgb:2626/2626/2626\x1b\\", "\x1b]10;rgb:d4d4/d4d4/d4d4\x1b\\" }, replies);
    }

    [TestMethod]
    public void MouseModesFollowDecset()
    {
        var t = new Terminal(10, 2);
        t.Feed("\x1b[?1000h\x1b[?1002h\x1b[?1003h\x1b[?1006h");   // Claude Code's sequence
        Assert.AreEqual(MouseTracking.Any, t.Mouse);
        Assert.IsTrue(t.SgrMouse);
        t.Feed("\x1b[?1003l");
        Assert.AreEqual(MouseTracking.None, t.Mouse);
    }

    [TestMethod]
    public void MouseReportsInSgrAndX10()
    {
        Assert.AreEqual("\u001b[<0;5;3M", MouseEncoder.Encode(MouseTracking.Click, true, MouseAction.Press, 0, 4, 2, false, false, false));
        Assert.AreEqual("\u001b[<0;5;3m", MouseEncoder.Encode(MouseTracking.Click, true, MouseAction.Release, 0, 4, 2, false, false, false));
        Assert.AreEqual("\u001b[<64;1;1M", MouseEncoder.Encode(MouseTracking.Click, true, MouseAction.WheelUp, -1, 0, 0, false, false, false));
        Assert.AreEqual("\u001b[<81;2;2M", MouseEncoder.Encode(MouseTracking.Click, true, MouseAction.WheelDown, -1, 1, 1, false, false, true));   // 65 + ctrl 16
        Assert.IsNull(MouseEncoder.Encode(MouseTracking.Click, true, MouseAction.Move, 0, 1, 1, false, false, false));      // drag needs 1002
        Assert.AreEqual("\u001b[<32;2;2M", MouseEncoder.Encode(MouseTracking.Drag, true, MouseAction.Move, 0, 1, 1, false, false, false));
        Assert.IsNull(MouseEncoder.Encode(MouseTracking.Drag, true, MouseAction.Move, -1, 1, 1, false, false, false));      // hover needs 1003
        Assert.AreEqual("\u001b[<35;2;2M", MouseEncoder.Encode(MouseTracking.Any, true, MouseAction.Move, -1, 1, 1, false, false, false));
        Assert.AreEqual("\u001b[M !!", MouseEncoder.Encode(MouseTracking.Click, false, MouseAction.Press, 0, 0, 0, false, false, false));
        Assert.AreEqual("\u001b[M#!!", MouseEncoder.Encode(MouseTracking.Click, false, MouseAction.Release, 0, 0, 0, false, false, false));
        Assert.IsNull(MouseEncoder.Encode(MouseTracking.None, true, MouseAction.Press, 0, 0, 0, false, false, false));
    }

    [TestMethod]
    public void AgentStateFromProgressAndClaudeTitles()
    {
        Assert.AreEqual(AgentState.Working, AgentStatus.FromProgress(3));
        Assert.AreEqual(AgentState.Error, AgentStatus.FromProgress(2));
        Assert.AreEqual(AgentState.Idle, AgentStatus.FromProgress(0));
        Assert.IsTrue(AgentStatus.TitleShowsWork("◑ armux"));
        Assert.IsTrue(AgentStatus.TitleShowsWork("⠋ Claude Code"));
        Assert.IsFalse(AgentStatus.TitleShowsWork("✳ Claude Code"));
        Assert.IsFalse(AgentStatus.TitleShowsWork("pwsh"));
        Assert.AreEqual(AgentState.NeedsInput, AgentStatus.Max([AgentState.Working, AgentState.NeedsInput, AgentState.Idle]));
        Assert.AreEqual(AgentState.Idle, AgentStatus.Max([]));
    }
}
