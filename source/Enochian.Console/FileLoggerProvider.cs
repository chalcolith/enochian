using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Enochian.Console;

internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter writer;
    private readonly Lock writeLock = new();

    public FileLoggerProvider(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        writer = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, writer, writeLock);
    }

    public void Dispose()
    {
        writer.Dispose();
    }

    private sealed class FileLogger(string categoryName, TextWriter writer, Lock writeLock) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            lock (writeLock)
            {
                writer.Write(DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                writer.Write(' ');
                writer.Write(categoryName);
                writer.Write(' ');
                writer.WriteLine(formatter(state, exception));
                if (exception != null)
                {
                    writer.WriteLine(exception);
                }
            }
        }
    }
}
