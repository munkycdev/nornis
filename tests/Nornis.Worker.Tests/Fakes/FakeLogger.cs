using Microsoft.Extensions.Logging;

namespace Nornis.Worker.Tests.Fakes;

/// <summary>
/// A fake logger that captures all log entries for assertion in unit tests.
/// Records log level, message template, and formatted message to enable
/// verification of structured logging fields.
/// </summary>
public sealed class FakeLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var template = state?.ToString() ?? string.Empty;
        _entries.Add(new LogEntry(logLevel, template, message, exception));
    }

    public bool HasLoggedError() =>
        _entries.Any(e => e.Level == LogLevel.Error);

    public bool HasLoggedContaining(string text) =>
        _entries.Any(e =>
            e.Template.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            e.Message.Contains(text, StringComparison.OrdinalIgnoreCase));

    public record LogEntry(LogLevel Level, string Template, string Message, Exception? Exception);
}
