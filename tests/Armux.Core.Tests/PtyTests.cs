using Armux.Core.Pty;
using Armux.Core.Vt;

namespace Armux.Core.Tests;

[TestClass]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class PtyTests
{
    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void CmdOutputReachesTheTerminalAndExitIsReported()
    {
        var term = new Terminal(80, 10);
        using var exited = new ManualResetEventSlim();
        int code = -1;
        var env = new Dictionary<string, string> { ["ARMUX_TEST_VALUE"] = "from-armux" };
        using var pty = PtySession.Start("cmd.exe /c echo pty-ok %ARMUX_TEST_VALUE% & exit 3", Path.GetTempPath(), env, 80, 10,
            data => term.Feed(data.Span), c => { code = c; exited.Set(); });

        Assert.IsTrue(exited.Wait(TimeSpan.FromSeconds(20)), "cmd did not exit");
        Assert.AreEqual(3, code);
        // Output can trail the exit notification briefly.
        string text = "";
        for (int i = 0; i < 40 && !text.Contains("pty-ok from-armux"); i++)
        {
            Thread.Sleep(50);
            lock (term.Sync) text = term.GetText(0, 0, term.LineCount - 1, term.Cols - 1);
        }
        StringAssert.Contains(text, "pty-ok from-armux");
    }
}
