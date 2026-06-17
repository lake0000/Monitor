namespace DiskGrowthMonitor.Core;

public static class SourceGuesser
{
    public static (string Source, double Confidence) Guess(string path)
    {
        var value = path.Replace('/', '\\');
        return value switch
        {
            var p when ContainsSegment(p, "Downloads") => ("下载文件", 0.85),
            var p when ContainsSegment(p, "Temp") => ("临时文件", 0.82),
            var p when ContainsSegment(p, "Cache") || ContainsSegment(p, "Cache_Data") => ("缓存", 0.82),
            var p when ContainsSegment(p, "Logs") || ContainsSegment(p, "Log") => ("日志", 0.78),
            var p when ContainsSegment(p, "CrashDumps") => ("崩溃转储", 0.78),
            var p when p.Contains("Chrome", StringComparison.OrdinalIgnoreCase) => ("浏览器缓存/数据", 0.72),
            var p when p.Contains("WeChat", StringComparison.OrdinalIgnoreCase) || p.Contains("Tencent", StringComparison.OrdinalIgnoreCase) => ("微信/腾讯数据", 0.68),
            _ => ("未知来源", 0.35)
        };
    }

    private static bool ContainsSegment(string path, string segment)
    {
        return path.Split('\\', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.Equals(segment, StringComparison.OrdinalIgnoreCase));
    }
}
