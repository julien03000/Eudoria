using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Eudoria.Core.Api.Tests.Infrastructure;

/// <summary>Keeps every log entry (message, structured values and exception) in memory, for assertions.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();
    private readonly ConcurrentQueue<LogLevel> _levels = new();

    /// <summary>Every entry as one text: category, message, structured values, exception.</summary>
    public IReadOnlyCollection<string> Entries => _entries;

    /// <summary>The level of every entry.</summary>
    public IReadOnlyCollection<LogLevel> Levels => _levels;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries, _levels);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> entries, ConcurrentQueue<LogLevel> levels) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(", ", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                : string.Empty;
            levels.Enqueue(logLevel);
            entries.Enqueue($"{category} | {formatter(state, exception)} | {values} | {exception}");
        }
    }
}
