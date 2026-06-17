namespace DiskGrowthMonitor.Core;

public static class AppPaths
{
    public const string AppFolderName = "DiskGrowthMonitor";

    public static string DefaultDataDirectory()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, AppFolderName);
    }

    public static string DefaultDatabasePath()
    {
        return Path.Combine(DefaultDataDirectory(), "monitor.db");
    }

    public static string DefaultExportDirectory()
    {
        return Path.Combine(DefaultDataDirectory(), "exports");
    }
}
