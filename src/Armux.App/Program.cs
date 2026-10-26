using System.Diagnostics;

namespace Armux.App;

/// <summary>
/// Entry point. The app is armux.exe and the CLI is armux.com in the same folder: PowerShell and cmd run .com before
/// .exe, so `armux notify` reaches the CLI. Shells that ignore PATHEXT (Git Bash) start armux.exe instead, so a CLI
/// command line is handed to armux.com here, before any UI starts.
/// </summary>
public static class Program
{
    static readonly HashSet<string> CliVerbs = ["notify", "send", "open", "list", "ping", "help", "--help", "-h", "--version", "-v", "version"];

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && CliVerbs.Contains(args[0])) return RunCli(args);
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }

    /// <summary>Runs armux.com with the same arguments, copies its output to this process's standard handles, and returns its exit code.</summary>
    static int RunCli(string[] args)
    {
        // Without redirection a child of this GUI process would get a hidden console of its own, not the caller's pipes.
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "armux.com"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(info)!;
            using var stdout = Console.OpenStandardOutput();
            using var stderr = Console.OpenStandardError();
            var copyOut = p.StandardOutput.BaseStream.CopyToAsync(stdout);
            var copyErr = p.StandardError.BaseStream.CopyToAsync(stderr);
            p.WaitForExit();
            Task.WaitAll(copyOut, copyErr);
            return p.ExitCode;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException) { return 127; }
    }
}
