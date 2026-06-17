namespace DiskGrowthMonitor.Core;

public sealed class MonitorSettings
{
    public long DisplayThresholdBytes { get; set; } = 200L * 1024 * 1024;
    public long PersistThresholdBytes { get; set; } = 10L * 1024 * 1024;
    public long AlertThresholdBytes { get; set; } = 1024L * 1024 * 1024;
    public TimeSpan DebounceDelay { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan BatchInterval { get; set; } = TimeSpan.FromSeconds(7);
    public int RetentionDays { get; set; } = 30;
    public bool MinimizeOnStart { get; set; }
    public bool ShowSystemPathHints { get; set; } = true;
    public string DataDirectory { get; set; } = AppPaths.DefaultDataDirectory();
    public List<string> WatchDirectories { get; } = new();

    public string DatabasePath => Path.Combine(DataDirectory, "monitor.db");

    public static MonitorSettings CreateDefault()
    {
        var settings = new MonitorSettings();
        settings.WatchDirectories.AddRange(DefaultWatchDirectoryProvider.GetDefaultWatchDirectories());
        return settings;
    }
}
