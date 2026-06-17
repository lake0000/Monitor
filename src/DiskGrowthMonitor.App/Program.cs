using DiskGrowthMonitor.Core;

namespace DiskGrowthMonitor.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            StartupLog.Write("Process starting.");
            Application.ThreadException += (_, e) =>
            {
                StartupLog.Write(e.Exception);
                MessageBox.Show(e.Exception.Message, "启动异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception exception)
                {
                    StartupLog.Write(exception);
                }
            };

            ApplicationConfiguration.Initialize();
            if (args.Contains("--smoke-start", StringComparer.OrdinalIgnoreCase))
            {
                using var form = new MainForm(MonitorSettings.CreateDefault(), null, null, false);
                StartupLog.Write("Smoke start completed.");
                return;
            }

            Application.Run(new MainForm());
        }
        catch (Exception exception)
        {
            StartupLog.Write(exception);
            MessageBox.Show(exception.Message, "启动异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }    
}
