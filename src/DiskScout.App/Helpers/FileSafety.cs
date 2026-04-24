using System.IO;

namespace DiskScout.Helpers;

/// <summary>
/// Heuristic score 0..100: how safe is it to propose deletion?
/// 100 = fully safe (caches, temp), 0 = do NOT suggest (active project, git, OS-signed path).
/// </summary>
public static class FileSafety
{
    private static readonly string[] RiskyAncestors =
    {
        @"\Windows\System32",
        @"\Windows\SysWOW64",
        @"\Windows\WinSxS",
        @"\Program Files\Common Files",
        @"\Program Files (x86)\Common Files",
    };

    private static readonly string[] SafeAncestors =
    {
        @"\AppData\Local\Temp",
        @"\Windows\Temp",
        @"\$Recycle.Bin",
        @"\Windows\SoftwareDistribution\Download",
        @"\Windows\Prefetch",
    };

    private static readonly string[] ActiveProjectMarkers =
    {
        ".git", ".svn", ".hg",                // VCS
        "package.json", "pom.xml", "build.gradle", "Cargo.toml", "*.sln", "*.csproj", "*.sbt",
        "go.mod", "pyproject.toml", "setup.py", "requirements.txt",
    };

    public static int Score(string path)
    {
        if (string.IsNullOrEmpty(path)) return 50;

        // OS-critical → never suggest
        foreach (var risky in RiskyAncestors)
        {
            if (path.Contains(risky, StringComparison.OrdinalIgnoreCase)) return 0;
        }

        // Explicitly safe paths
        foreach (var safe in SafeAncestors)
        {
            if (path.Contains(safe, StringComparison.OrdinalIgnoreCase)) return 95;
        }

        // Git repo or active project → low suggestion
        try
        {
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (dir is not null && IsUnderActiveProject(dir)) return 10;
        }
        catch { /* best-effort */ }

        return 70; // neutral default
    }

    public static bool IsLikelySafeToSuggest(string path) => Score(path) >= 60;

    private static bool IsUnderActiveProject(string path)
    {
        var dir = new DirectoryInfo(path);
        int hops = 0;
        while (dir is not null && hops < 8)
        {
            try
            {
                // .git / .svn / .hg directory
                if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return true;
                if (Directory.Exists(Path.Combine(dir.FullName, ".svn"))) return true;
                if (Directory.Exists(Path.Combine(dir.FullName, ".hg"))) return true;

                foreach (var file in Directory.EnumerateFiles(dir.FullName))
                {
                    var name = Path.GetFileName(file);
                    if (string.Equals(name, "package.json", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(name, "pom.xml", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(name, "Cargo.toml", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(name, "go.mod", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(name, "build.gradle", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(name, "pyproject.toml", StringComparison.OrdinalIgnoreCase)) return true;
                    if (name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)) return true;
                    if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { /* unauthorized access etc., skip */ }

            dir = dir.Parent;
            hops++;
        }
        return false;
    }
}
