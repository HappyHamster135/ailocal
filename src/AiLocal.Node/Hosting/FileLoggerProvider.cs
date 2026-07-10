using Microsoft.Extensions.Logging;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Minimal rolling-file logger so a headless Host/Worker/Overseer (routinely
/// launched with no console window - see LocalNodeLauncher.StartCoreAsync's
/// CreateNoWindow, and AutoStartManager's login launch) keeps a real
/// day-to-day diagnostic trail instead of only ever having CrashLog's
/// unhandled-exceptions-only record. Deliberately tiny and dependency-free
/// (mirrors CrashLog.cs's own style) rather than pulling in a logging
/// library for what's fundamentally "append lines to a daily file per role,
/// delete anything older than two weeks".
///
/// One file per role (host-yyyyMMdd.log, worker-..., overseer-...) rather
/// than one shared file: Host/Worker/Overseer/Launcher commonly run as
/// separate OS processes sharing the same AILOCAL_DATA_DIR on one machine,
/// and per-role files avoid interleaved output and cross-process write
/// contention on a single shared file entirely.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    private readonly string _directory;
    private readonly string _rolePrefix;
    private readonly LogLevel _minLevel;
    private readonly object _gate = new();

    public FileLoggerProvider(string directory, string rolePrefix, LogLevel minLevel)
    {
        _directory = directory;
        _rolePrefix = rolePrefix;
        _minLevel = minLevel;

        try
        {
            Directory.CreateDirectory(_directory);
            PruneOldFiles();
        }
        catch { /* logging setup must never itself block startup */ }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    internal bool IsEnabled(LogLevel level) => level >= _minLevel && level != LogLevel.None;

    internal void Write(string category, LogLevel level, string message, Exception? exception)
    {
        try
        {
            var file = Path.Combine(_directory, $"{_rolePrefix}-{DateTime.UtcNow:yyyyMMdd}.log");
            var line = $"[{DateTimeOffset.UtcNow:O}] {LevelLabel(level)} {category}: {message}";
            if (exception is not null)
                line += Environment.NewLine + exception;

            lock (_gate)
                File.AppendAllText(file, line + Environment.NewLine);
        }
        catch { /* logging must never itself crash the process */ }
    }

    private void PruneOldFiles()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var file in Directory.GetFiles(_directory, $"{_rolePrefix}-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch { /* best effort - a locked/in-use file just survives this round */ }
        }
    }

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "FAIL",
        LogLevel.Critical => "CRIT",
        _ => level.ToString()
    };

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            _provider.Write(_category, logLevel, formatter(state, exception), exception);
        }
    }
}
