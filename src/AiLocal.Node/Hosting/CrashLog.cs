namespace AiLocal.Node.Hosting;

/// <summary>
/// Minimal crash diagnostics: writes unhandled exceptions to a log file under
/// %LOCALAPPDATA%\AiLocal\logs so a silent process exit is diagnosable instead
/// of just vanishing.
/// </summary>
public static class CrashLog
{
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("UnhandledException", e.ExceptionObject as Exception, e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    public static void Write(string source, Exception? ex, bool isTerminating = false)
    {
        try
        {
            var dir = Path.Combine(SettingsPaths.DataDirectory, "logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"crash-{DateTime.UtcNow:yyyyMMdd}.log");
            var entry =
                $"[{DateTimeOffset.UtcNow:O}] {source} (terminating={isTerminating}){Environment.NewLine}" +
                $"{ex}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";
            File.AppendAllText(file, entry);
        }
        catch
        {
            // Logging must never itself crash the process.
        }
    }
}
