namespace Armux.Core.Pty;

public enum ShellKind { PowerShell, Cmd }

/// <summary>The shells armux starts: PowerShell 7 when installed (else Windows PowerShell) and cmd. WSL is out of scope.</summary>
public static class Shells
{
    public static string Name(ShellKind kind) => kind == ShellKind.Cmd ? "cmd" : "pwsh";

    public static ShellKind Parse(string? name) => string.Equals(name, "cmd", StringComparison.OrdinalIgnoreCase) ? ShellKind.Cmd : ShellKind.PowerShell;

    public static string CommandLine(ShellKind kind) => kind switch
    {
        ShellKind.Cmd => "cmd.exe",
        _ => FindPwsh() is { } pwsh ? $"\"{pwsh}\" -NoLogo" : "powershell.exe -NoLogo",
    };

    /// <summary>pwsh.exe from the default install folder or PATH, or null.</summary>
    public static string? FindPwsh()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var standard = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
        if (File.Exists(standard)) return standard;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "pwsh.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }
}
