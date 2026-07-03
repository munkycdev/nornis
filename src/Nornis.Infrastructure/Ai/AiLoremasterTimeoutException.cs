namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Thrown when a Loremaster AI call exceeds the configured timeout.
/// The LoremasterService classifies this as a transient error (503).
/// </summary>
public class AiLoremasterTimeoutException : Exception
{
    public int DurationMs { get; }

    public AiLoremasterTimeoutException(string message, int durationMs)
        : base(message)
    {
        DurationMs = durationMs;
    }

    public AiLoremasterTimeoutException(string message, int durationMs, Exception innerException)
        : base(message, innerException)
    {
        DurationMs = durationMs;
    }
}
