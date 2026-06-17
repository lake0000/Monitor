namespace DiskGrowthMonitor.Core;

public sealed class GrowthTraceOptions
{
    public int MaxDepth { get; init; } = 8;
    public int MaxEventsPerNode { get; init; } = 200;
    public int MaxChildrenPerNode { get; init; } = 10;
    public long MinDeltaBytes { get; init; } = 10L * 1024 * 1024;
    public TimeSpan Window { get; init; } = TimeSpan.FromDays(1);
}

public sealed class GrowthTraceBuilder
{
    private static readonly HashSet<string> SemanticStopNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Temp",
        "Cache",
        "Cache_Data",
        "Downloads",
        "Logs",
        "Log",
        "CrashDumps"
    };

    private readonly SqliteMonitorStore _store;
    private readonly GrowthTraceOptions _options;

    public GrowthTraceBuilder(SqliteMonitorStore store, GrowthTraceOptions? options = null)
    {
        _store = store;
        _options = options ?? new GrowthTraceOptions();
    }

    public GrowthTraceNode Build(string groupPath)
    {
        var normalized = PathRules.NormalizeDirectory(groupPath);
        var events = _store.QueryEventsUnderGroup(normalized, _options.Window, _options.MaxEventsPerNode);
        return BuildNode(normalized, events, 0);
    }

    private GrowthTraceNode BuildNode(string path, IReadOnlyList<GrowthEvent> events, int depth)
    {
        var total = events.Sum(item => item.DeltaSize);
        var direct = events
            .Where(item => string.Equals(Path.GetDirectoryName(item.Path), path, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => Math.Abs(item.DeltaSize))
            .Take(8)
            .ToArray();

        var stopReason = GetStopReason(path, events, depth, total);
        if (!string.IsNullOrWhiteSpace(stopReason))
        {
            return new GrowthTraceNode(path, total, events.Count, depth, stopReason, Array.Empty<GrowthTraceNode>(), direct);
        }

        var childGroups = events
            .Select(item => new { Event = item, Child = GetNextChildPath(path, item.Path) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Child))
            .GroupBy(item => item.Child!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Path = group.Key,
                Events = group.Select(item => item.Event).ToArray(),
                Delta = group.Sum(item => item.Event.DeltaSize)
            })
            .Where(group => Math.Abs(group.Delta) >= _options.MinDeltaBytes)
            .OrderByDescending(group => Math.Abs(group.Delta))
            .Take(_options.MaxChildrenPerNode)
            .Select(group => BuildNode(group.Path, group.Events, depth + 1))
            .ToArray();

        var reason = childGroups.Length == 0 ? "没有超过阈值的下一级变化" : "";
        return new GrowthTraceNode(path, total, events.Count, depth, reason, childGroups, direct);
    }

    private string GetStopReason(string path, IReadOnlyList<GrowthEvent> events, int depth, long total)
    {
        if (depth >= _options.MaxDepth)
        {
            return "达到最大溯源深度";
        }

        if (events.Count >= _options.MaxEventsPerNode)
        {
            return "事件过多，已停止继续展开";
        }

        if (Math.Abs(total) < _options.MinDeltaBytes)
        {
            return "变化量低于溯源阈值";
        }

        if (PathRules.IsSensitiveSystemRoot(path))
        {
            return "系统敏感目录";
        }

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (SemanticStopNames.Contains(name))
        {
            return "目录类型已明确";
        }

        return "";
    }

    private static string? GetNextChildPath(string root, string eventPath)
    {
        var normalizedRoot = PathRules.NormalizeDirectory(root);
        var fullEventPath = Path.GetFullPath(eventPath);
        var eventDirectory = Directory.Exists(fullEventPath) ? fullEventPath : Path.GetDirectoryName(fullEventPath);
        if (string.IsNullOrWhiteSpace(eventDirectory) || !PathRules.IsSameOrChild(eventDirectory, normalizedRoot))
        {
            return null;
        }

        var relative = Path.GetRelativePath(normalizedRoot, eventDirectory);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return null;
        }

        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part));
        return string.IsNullOrWhiteSpace(first) ? null : Path.Combine(normalizedRoot, first);
    }
}
