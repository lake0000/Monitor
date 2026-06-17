namespace DiskGrowthMonitor.Core;

public static class DirectoryGrouper
{
    private static readonly HashSet<string> StopNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Temp",
        "Cache",
        "Cache_Data",
        "Downloads",
        "Logs",
        "Log",
        "CrashDumps"
    };

    public static string GetGroupPath(string path, IReadOnlyCollection<string> watchRoots, int maxDepth = 3)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath) ?? fullPath;

        var matchingRoot = watchRoots
            .Select(PathRules.NormalizeDirectory)
            .Where(root => PathRules.IsSameOrChild(directory, root))
            .OrderByDescending(root => root.Length)
            .FirstOrDefault();

        if (matchingRoot is null)
        {
            return TrimToDepth(directory, maxDepth);
        }

        var relative = Path.GetRelativePath(matchingRoot, directory);
        if (relative == "." || string.IsNullOrWhiteSpace(relative))
        {
            return matchingRoot;
        }

        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        var selected = new List<string>();
        foreach (var part in parts)
        {
            selected.Add(part);
            if (selected.Count >= 2 || StopNames.Contains(part))
            {
                break;
            }
        }

        return Path.Combine(new[] { matchingRoot }.Concat(selected).ToArray());
    }

    private static string TrimToDepth(string directory, int maxDepth)
    {
        var root = Path.GetPathRoot(directory) ?? "";
        var rest = directory[root.Length..]
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Take(maxDepth)
            .ToArray();
        return Path.Combine(new[] { root.TrimEnd(Path.DirectorySeparatorChar) }.Concat(rest).ToArray());
    }
}
