using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MobileApp.Debug;

public sealed class InMemoryLoggerProvider(IDebugStore<DebugLogEntry> store) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, InMemoryLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new InMemoryLogger(name, store));

    public void Dispose() => _loggers.Clear();

    private sealed class InMemoryLogger(string category, IDebugStore<DebugLogEntry> store) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            try
            {
                var message = formatter(state, exception);
                store.Add(new DebugLogEntry(DateTimeOffset.UtcNow, logLevel, category, message, exception?.ToString()));
            }
            catch
            {
                // Log capture must never break the real logging pipeline.
            }
        }
    }
}
