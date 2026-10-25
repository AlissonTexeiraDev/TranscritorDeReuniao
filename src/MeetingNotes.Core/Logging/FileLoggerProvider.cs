using Microsoft.Extensions.Logging;

namespace MeetingNotes.Logging;

/// <summary>Logger mínimo em arquivo (uma linha por evento, com rotação simples) para o cliente de bandeja.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _lock = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public FileLoggerProvider(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    private void Write(string line)
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                    File.Move(_path, _path + ".old", overwrite: true);
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch
            {
                // log nunca derruba o app
            }
        }
    }

    public void Dispose() { }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {Abbrev(logLevel)} {_category}: {formatter(state, exception)}";
            if (exception is not null) line += $" | {exception.GetType().Name}: {exception.Message}";
            _provider.Write(line);
        }

        private static string Abbrev(LogLevel l) => l switch
        {
            LogLevel.Trace => "trc",
            LogLevel.Debug => "dbg",
            LogLevel.Information => "inf",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
    }
}
