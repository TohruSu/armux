using Armux.Core.Vt;

namespace Armux.Core.Tests;

[TestClass]
public sealed class TerminalTests
{
    static string Line(Terminal t, int screenRow) => t.GetText(t.ScrollbackCount + screenRow, 0, t.ScrollbackCount + screenRow, t.Cols - 1);

    static Cell At(Terminal t, int x, int y)
    {
        var buf = new Cell[t.Cols];
        t.ReadLine(t.ScrollbackCount + y, buf);
        return buf[x];
    }

    [TestMethod]
    public void PrintsAndWrapsAtTheRightEdge()
    {
        var t = new Terminal(5, 3);
        t.Feed("hello world");
        Assert.AreEqual("hello", Line(t, 0));
        Assert.AreEqual(" worl", Line(t, 1));
        Assert.AreEqual("d", Line(t, 2));
        Assert.IsTrue(t.IsWrapped(0));
        Assert.AreEqual("hello world", t.GetText(0, 0, 2, 4));
    }

    [TestMethod]
    public void CursorMovesAndErases()
    {
        var t = new Terminal(10, 3);
        t.Feed("abcdefghij\r\n0123456789");
        t.Feed("\x1b[1;3H\x1b[K");          // row 1 col 3, erase to end of line
        Assert.AreEqual("ab", Line(t, 0));
        t.Feed("\x1b[2;5H\x1b[1K");         // erase start of row 2 through col 5
        Assert.AreEqual("     56789", t.GetText(1, 0, 1, 9));
        t.Feed("\x1b[2J");
        Assert.AreEqual("", Line(t, 1));
        Assert.AreEqual(1, t.CursorY);
        Assert.AreEqual(4, t.CursorX);
    }

    [TestMethod]
    public void SgrColorsInAllForms()
    {
        var t = new Terminal(10, 1);
        t.Feed("\x1b[1;31ma\x1b[38;5;208mb\x1b[38;2;1;2;3mc\x1b[38:2::4:5:6md\x1b[48:5:17me\x1b[0mf");
        Assert.AreEqual(VtColor.Indexed(1), At(t, 0, 0).Attr.Fg);
        Assert.IsTrue(At(t, 0, 0).Attr.Flags.HasFlag(CellFlags.Bold));
        Assert.AreEqual(VtColor.Indexed(208), At(t, 1, 0).Attr.Fg);
        Assert.AreEqual(VtColor.Rgb(1, 2, 3), At(t, 2, 0).Attr.Fg);
        Assert.AreEqual(VtColor.Rgb(4, 5, 6), At(t, 3, 0).Attr.Fg);
        Assert.AreEqual(VtColor.Indexed(17), At(t, 4, 0).Attr.Bg);
        Assert.AreEqual(Attr.Default, At(t, 5, 0).Attr);
    }

    [TestMethod]
    public void LinesScrollIntoScrollbackAndKeepAttributes()
    {
        var t = new Terminal(10, 3, scrollbackLines: 2);
        t.Feed("\x1b[32m1\x1b[0m\r\n2\r\n3\r\n4\r\n5\r\n6");
        Assert.AreEqual(2, t.ScrollbackCount);
        Assert.AreEqual("2", t.GetText(0, 0, 0, 9));   // "1" was dropped: capacity is 2
        Assert.AreEqual("6", Line(t, 2));

        var t2 = new Terminal(10, 1);
        t2.Feed("\x1b[32mgr\x1b[0meen\r\nx");
        var buf = new Cell[10];
        t2.ReadLine(0, buf);
        Assert.AreEqual(VtColor.Indexed(2), buf[1].Attr.Fg);
        Assert.AreEqual(Attr.Default, buf[2].Attr);
        Assert.AreEqual('n', buf[4].Rune);
    }

    [TestMethod]
    public void AlternateScreenRestoresMainScreenAndCursor()
    {
        var t = new Terminal(10, 3);
        t.Feed("main\r\nline");
        t.Feed("\x1b[?1049h");
        Assert.IsTrue(t.AltScreen);
        Assert.AreEqual("", Line(t, 0));
        t.Feed("\x1b[Hvim");
        t.Feed("\x1b[?1049l");
        Assert.IsFalse(t.AltScreen);
        Assert.AreEqual("main", Line(t, 0));
        Assert.AreEqual((4, 1), (t.CursorX, t.CursorY));
    }

    [TestMethod]
    public void OscEventsTitleCwdAndNotifications()
    {
        var t = new Terminal(20, 2);
        string? title = null, cwd = null;
        var notes = new List<TermNotification>();
        t.TitleChanged += s => title = s;
        t.CwdChanged += s => cwd = s;
        t.Notified += notes.Add;

        t.Feed("\x1b]0;my title\x07\x1b]7;file://host/C:/Users/dev%20x\x1b\\");
        t.Feed("\x1b]9;build done\x07\x1b]9;4;1;50\x07");                       // ConEmu progress is not a notification
        t.Feed("\x1b]777;notify;Claude;waiting for input\x07");
        t.Feed("\x1b]99;i=7:d=0;Tests\x1b\\\x1b]99;i=7:p=body;24 passed\x1b\\");   // kitty, two chunks
        t.Feed("\x1b]99;e=1;" + Convert.ToBase64String("hi"u8.ToArray()) + "\x1b\\");

        Assert.AreEqual("my title", title);
        Assert.AreEqual(@"C:\Users\dev x", cwd);
        Assert.HasCount(4, notes);
        Assert.AreEqual(new TermNotification("", "build done"), notes[0]);
        Assert.AreEqual(new TermNotification("Claude", "waiting for input"), notes[1]);
        Assert.AreEqual(new TermNotification("Tests", "24 passed"), notes[2]);
        Assert.AreEqual(new TermNotification("", "hi"), notes[3]);
        Assert.AreEqual("", Line(t, 0));   // nothing printed
    }

    [TestMethod]
    public void RepliesToStatusQueries()
    {
        var t = new Terminal(10, 5);
        var replies = new List<string>();
        t.Reply += replies.Add;
        t.Feed("\x1b[3;4H\x1b[6n\x1b[5n\x1b[c");
        CollectionAssert.AreEqual(new[] { "\x1b[3;4R", "\x1b[0n", "\x1b[?1;2c" }, replies);
    }

    [TestMethod]
    public void WideCharactersTakeTwoCellsAndSplitUtf8IsJoined()
    {
        var t = new Terminal(6, 1);
        var bytes = System.Text.Encoding.UTF8.GetBytes("a中b");
        t.Feed(bytes.AsSpan(0, 2));   // splits the 3-byte '中'
        t.Feed(bytes.AsSpan(2));
        Assert.AreEqual('中', At(t, 1, 0).Rune);
        Assert.AreEqual(Cell.WideTail, At(t, 2, 0).Rune);
        Assert.AreEqual('b', At(t, 3, 0).Rune);
        Assert.AreEqual("a中b", Line(t, 0));
    }

    [TestMethod]
    public void ScrollRegionAndInsertDeleteLines()
    {
        var t = new Terminal(5, 4);
        t.Feed("1\r\n2\r\n3\r\n4");
        t.Feed("\x1b[2;3r\x1b[2;1H\x1b[M");   // region rows 2-3, delete line 2
        Assert.AreEqual("1", Line(t, 0));
        Assert.AreEqual("3", Line(t, 1));
        Assert.AreEqual("", Line(t, 2));
        Assert.AreEqual("4", Line(t, 3));
        Assert.AreEqual(0, t.ScrollbackCount);
    }

    [TestMethod]
    public void ResizeKeepsTheCursorRowOnScreen()
    {
        var t = new Terminal(10, 4);
        t.Feed("a\r\nb\r\nc\r\nd");
        t.Resize(8, 2);
        Assert.AreEqual(2, t.ScrollbackCount);
        Assert.AreEqual("c", Line(t, 0));
        Assert.AreEqual("d", Line(t, 1));
        Assert.AreEqual(1, t.CursorY);
    }

    [TestMethod]
    public void ConPtyLongLineWrapsWithoutLosingTheEdgeCharacter()
    {
        // Captured from ConPTY at 30 columns: one long line, no explicit line breaks.
        var t = new Terminal(30, 10);
        t.Feed("\x1b[?9001h\x1b[?1004h\x1b[?25l\x1b[2J\x1b[m\x1b[H0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJ is not recognized as executable program\r\n\x1b]0;cmd\x07\x1b[?25h");
        Assert.AreEqual("0123456789abcdefghijklmnopqrst", Line(t, 0));
        Assert.AreEqual("uvwxyzABCDEFGHIJ is not recogn", Line(t, 1));
        Assert.AreEqual("ized as executable program", Line(t, 2));
    }

    [TestMethod]
    public void NarrowingReflowsWrappedTextAndKeepsTheCursor()
    {
        var t = new Terminal(20, 4);
        t.Feed("PS> armux list\r\nok\r\nPS> ");
        t.Resize(8, 4);
        Assert.AreEqual("PS> armu", Line(t, 0));
        Assert.AreEqual("PS> armux list", t.GetText(0, 0, 1, 7));
        Assert.AreEqual("ok", Line(t, 2));
        Assert.AreEqual("PS>", Line(t, 3));
        Assert.AreEqual((4, 3), (t.CursorX, t.CursorY));
    }

    [TestMethod]
    public void WideningJoinsRowsThatWereWrapped()
    {
        var t = new Terminal(5, 4);
        t.Feed("hello world\r\nnext");
        Assert.AreEqual("hello", Line(t, 0));
        t.Resize(20, 4);
        Assert.AreEqual("hello world", Line(t, 0));
        Assert.AreEqual("next", Line(t, 1));
        Assert.AreEqual((4, 1), (t.CursorX, t.CursorY));
        Assert.IsFalse(t.IsWrapped(0));
    }

    [TestMethod]
    public void DecLineDrawingCharset()
    {
        var t = new Terminal(5, 1);
        t.Feed("\x1b(0lqk\x1b(Bq");
        Assert.AreEqual("┌─┐q", Line(t, 0));
    }
}
