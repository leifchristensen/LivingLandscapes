using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Which code is running: the mod folder the game loaded, its git branch and commit, and a hash of the loaded DLL.
/// Logged at startup and written to every test summary, so a result always names the build it came from (several
/// worktrees can share one game install).
/// </summary>
public static class BuildStamp
{
    private static string text;

    public static string Text => text ??= Build();

    private static string Build()
    {
        try
        {
            string dll = Assembly.GetExecutingAssembly().Location;
            string modRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dll), "..", ".."));
            string hash;
            using (var sha = SHA1.Create())
            using (FileStream stream = File.OpenRead(dll))
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").Substring(0, 10).ToLowerInvariant();
            return $"build {GitHead(modRoot)} | dll {hash} built {File.GetLastWriteTime(dll):yyyy-MM-dd HH:mm:ss} | mod folder {modRoot}";
        }
        catch (Exception e)
        {
            return $"build unknown ({e.GetType().Name})";
        }
    }

    /// <summary>Branch and short commit of the checkout (a plain repo or a worktree), read from the git files directly.</summary>
    private static string GitHead(string root)
    {
        string git = Path.Combine(root, ".git");
        string gitDir = git;
        if (File.Exists(git))
        {
            string line = File.ReadAllText(git).Trim();
            if (line.StartsWith("gitdir:"))
                gitDir = Path.GetFullPath(Path.Combine(root, line.Substring(7).Trim()));
        }
        string headFile = Path.Combine(gitDir, "HEAD");
        if (!File.Exists(headFile))
            return "(not a git checkout)";
        string head = File.ReadAllText(headFile).Trim();
        if (!head.StartsWith("ref:"))
            return $"(detached) {head.Substring(0, Math.Min(8, head.Length))}";
        string reference = head.Substring(4).Trim();
        string branch = reference.StartsWith("refs/heads/") ? reference.Substring(11) : reference;
        return $"{branch} @ {Commit(gitDir, reference)}";
    }

    private static string Commit(string gitDir, string reference)
    {
        // A worktree keeps its refs in the main repository, named by the commondir file.
        string common = gitDir;
        string commonFile = Path.Combine(gitDir, "commondir");
        if (File.Exists(commonFile))
            common = Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(commonFile).Trim()));
        string refFile = Path.Combine(common, reference.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(refFile))
            return File.ReadAllText(refFile).Trim().Substring(0, 8);
        string packed = Path.Combine(common, "packed-refs");
        if (File.Exists(packed))
        {
            foreach (string line in File.ReadAllLines(packed))
            {
                if (line.EndsWith(" " + reference))
                    return line.Substring(0, 8);
            }
        }
        return "?";
    }
}
