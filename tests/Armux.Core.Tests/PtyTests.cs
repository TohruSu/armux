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

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void ProcessTreeFindsAShellsRunningProgram()
    {
        // The close warning lists what each shell has started; here the "shell" is cmd and the program is ping.
        using var pty = PtySession.Start("cmd.exe /c ping -n 20 127.0.0.1", Path.GetTempPath(), new Dictionary<string, string>(), 80, 10, _ => { }, _ => { });
        List<string> names = [];
        for (int i = 0; i < 100 && !names.Contains("ping"); i++)
        {
            Thread.Sleep(50);
            names = ProcessTree.ChildNames(ProcessTree.Snapshot(), pty.ProcessId).ToList();
        }
        CollectionAssert.Contains(names, "ping");
        Assert.IsFalse(ProcessTree.ChildNames(ProcessTree.Snapshot(), int.MaxValue - 1).Any());
    }
}
