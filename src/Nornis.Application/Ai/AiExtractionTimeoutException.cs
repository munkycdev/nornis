namespace Nornis.Application.Ai;

/// <summary>
/// Thrown when an AI extraction call exceeds the configured timeout.
/// The ExtractionService classifies this as a transient error.
/// </summary>
public class AiExtractionTimeoutException : Exception
{
    public int DurationMs { get; }

    public AiExtractionTimeoutException(string message, int durationMs)
        : base(message)
    {
        DurationMs = durationMs;
    }

    public AiExtractionTimeoutException(string message, int durationMs, Exception innerException)
        : base(message, innerException)
    {
        DurationMs = durationMs;
    }
}
