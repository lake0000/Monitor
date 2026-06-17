using DiskGrowthMonitor.Core;

namespace DiskGrowthMonitor.App;

internal static class StartupLog
{
    private static readonly object Sync = new();

    public static string LogPath => Path.Combine(AppPaths.DefaultDataDirectory(), "startup.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DefaultDataDirectory());
            lock (Sync)
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Startup logging must never prevent the app from opening.
        }
    }

    public static void Write(Exception exception)
    {
        Write(exception.ToString());
    }
}
