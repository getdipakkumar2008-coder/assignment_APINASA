using Microsoft.Extensions.Logging;

namespace MarsPhotos.Cli;

/// <summary>Writes logs to stderr so stdout stays clean for the summary / JSON. Every line is passed through a redactor.</summary>
internal sealed class StderrLoggerProvider(TextWriter writer, Func<string?, string> redact) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new StderrLogger(categoryName, writer, redact);

    public void Dispose()
    {
    }

    private sealed class StderrLogger(string category, TextWriter writer, Func<string?, string> redact) : ILogger
    {
        private static readonly object Gate = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message += $" ({exception.GetType().Name}: {exception.Message})";
            }

            lock (Gate)
            {
                writer.WriteLine(redact($"[{Short(logLevel)}] {category}: {message}"));
            }
        }

        private static string Short(LogLevel level) => level switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "none",
        };
    }
}
