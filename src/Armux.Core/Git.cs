namespace Armux.Core;

/// <summary>Reads the current git branch from .git/HEAD without starting git, for the sidebar's "branch · directory" line.</summary>
public static class Git
{
    /// <summary>
    /// The branch checked out in the repository containing <paramref name="dir"/>, a short commit id when HEAD is
    /// detached, or null outside a repository. Worktrees (a .git file with "gitdir:") are followed.
    /// </summary>
    public static string? Branch(string dir)
    {
        try
        {
            for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
            {
                var git = Path.Combine(d.FullName, ".git");
                string? gitDir = Directory.Exists(git) ? git
                    : File.Exists(git) && File.ReadAllText(git).Trim() is var link && link.StartsWith("gitdir:")
                        ? Path.GetFullPath(link[7..].Trim(), d.FullName) : null;
                if (gitDir is null) continue;
                var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
                const string prefix = "ref: refs/heads/";
                return head.StartsWith(prefix) ? head[prefix.Length..] : head.Length >= 7 ? head[..7] : head;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        return null;
    }
}
