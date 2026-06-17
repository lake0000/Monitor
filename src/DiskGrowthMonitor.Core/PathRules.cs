namespace DiskGrowthMonitor.Core;

public enum WatchPathValidationStatus
{
    Allowed,
    Missing,
    Inaccessible,
    Duplicate,
    SensitiveSystemRoot,
    ToolDataDirectory
}

public sealed record WatchPathValidationResult(WatchPathValidationStatus Status, string Message)
{
    public bool IsAllowed => Status == WatchPathValidationStatus.Allowed;
}

public static class PathRules
{
    private static readonly string[] SensitiveRoots =
    {
        @"C:\Windows",
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\System Volume Information",
        @"C:\$Recycle.Bin",
        @"C:\Recovery",
        @"C:\Boot"
    };

    public static bool IsSensitiveSystemRoot(string path)
    {
        var fullPath = NormalizeDirectory(path);
        return SensitiveRoots.Any(root => string.Equals(fullPath, NormalizeDirectory(root), StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsUnderSensitiveSystemPath(string path)
    {
        var fullPath = NormalizeDirectory(path);
        return SensitiveRoots.Any(root => IsSameOrChild(fullPath, NormalizeDirectory(root)));
    }

    public static bool IsSameOrChild(string candidate, string parent)
    {
        var fullCandidate = NormalizeDirectory(candidate);
        var fullParent = NormalizeDirectory(parent);
        return fullCandidate.Equals(fullParent, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static WatchPathValidationResult ValidateWatchDirectory(string path, IEnumerable<string> existingPaths, string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new WatchPathValidationResult(WatchPathValidationStatus.Missing, "路径不能为空。");
        }

        string fullPath;
        try
        {
            fullPath = NormalizeDirectory(path);
        }
        catch
        {
            return new WatchPathValidationResult(WatchPathValidationStatus.Missing, "路径格式无效。");
        }

        if (!Directory.Exists(fullPath))
        {
            return new WatchPathValidationResult(WatchPathValidationStatus.Missing, "路径不存在。");
        }

        try
        {
            _ = Directory.EnumerateFileSystemEntries(fullPath).Take(1).ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return new WatchPathValidationResult(WatchPathValidationStatus.Inaccessible, "路径无权限访问。");
        }
        catch (IOException)
        {
            return new WatchPathValidationResult(WatchPathValidationStatus.Inaccessible, "路径暂时不可访问。");
        }

        if (existingPaths.Any(existing => string.Equals(NormalizeDirectory(existing), fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            return new WatchPathValidationResult(WatchPathValidationStatus.Duplicate, "路径已在监听列表中。");
        }

        if (IsSensitiveSystemRoot(fullPath))
        {
            return new WatchPathValidationResult(WatchPathValidationStatus.SensitiveSystemRoot, "该目录属于系统目录，第一版本不建议监听，已阻止添加。");
        }

        if (!string.IsNullOrWhiteSpace(dataDirectory) && IsSameOrChild(fullPath, dataDirectory))
        {
            return new WatchPathValidationResult(WatchPathValidationStatus.ToolDataDirectory, "不能监听工具自身数据库目录。");
        }

        return new WatchPathValidationResult(WatchPathValidationStatus.Allowed, "允许监听。");
    }

    public static string NormalizeDirectory(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
