using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Blazejewicz.A2Ui.TestHelpers;

/// <summary>
/// In-memory <see cref="ILoggerProvider"/> that captures every log entry
/// emitted during a test as a record. Use <see cref="Entries"/> to query
/// captured logs via LINQ, asserting on <c>EventId</c> and structured
/// property values rather than formatted message strings.
/// </summary>
/// <remarks>
/// Wire into a test like this:
/// <code>
/// using var provider = new TestLoggerProvider();
/// var sut = new SurfaceManager(provider.CreateFactory());
/// sut.Process(message);
/// var entries = provider.Entries.Where(e => e.EventId.Id == 9).ToList();
/// </code>
/// Thread-safe for concurrent writes via <see cref="ConcurrentQueue{T}"/>.
/// <para>
/// <see cref="CreateFactory"/> returns a lightweight <see cref="ILoggerFactory"/> backed by this
/// provider without requiring a package reference to <c>Microsoft.Extensions.Logging</c>.
/// </para>
/// </remarks>
public sealed class TestLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<TestLogEntry> _entries = new();

    /// <summary>All log entries captured so far, in the order they were emitted.</summary>
    public IReadOnlyCollection<TestLogEntry> Entries => this._entries;

    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, this._entries);

    /// <summary>
    /// Create an <see cref="ILoggerFactory"/> that routes every logger to
    /// this provider's entry queue. The returned factory is owned by the
    /// caller and should be disposed together with the test.
    /// </summary>
    public ILoggerFactory CreateFactory() => new TestLoggerFactory(this);

    public void Dispose()
    {
        /* nothing to release — entries snapshot survives */
    }

    private sealed class TestLoggerFactory(TestLoggerProvider provider) : ILoggerFactory
    {
        private readonly TestLoggerProvider _provider = provider;

        public void AddProvider(ILoggerProvider provider)
        {
            // No-op: tests intentionally route to a single provider.
        }

        public ILogger CreateLogger(string categoryName) => this._provider.CreateLogger(categoryName);

        public void Dispose() { }
    }

    private sealed class TestLogger(string categoryName, ConcurrentQueue<TestLogEntry> sink) : ILogger
    {
        private readonly string _categoryName = categoryName;
        private readonly ConcurrentQueue<TestLogEntry> _sink = sink;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            // Extract structured properties if state is IEnumerable<KeyValuePair<string, object?>>
            // which is how LoggerMessage source-generated state appears.
            Dictionary<string, object?> properties = [];
            if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
            {
                foreach (var kvp in kvps)
                {
                    properties[kvp.Key] = kvp.Value;
                }
            }

            string message = formatter(state, exception);

            this._sink.Enqueue(
                new TestLogEntry(
                    CategoryName: this._categoryName,
                    Level: logLevel,
                    EventId: eventId,
                    Message: message,
                    Properties: properties,
                    Exception: exception
                )
            );
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}
