namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Thrown when the AI service returns HTTP 429 (Too Many Requests).
/// The LoremasterService classifies this as a rate limit error (429).
/// </summary>
public class AiLoremasterRateLimitException : Exception
{
    public int DurationMs { get; }

    public AiLoremasterRateLimitException(string message, int durationMs)
        : base(message)
    {
        DurationMs = durationMs;
    }

    public AiLoremasterRateLimitException(string message, int durationMs, Exception innerException)
        : base(message, innerException)
    {
        DurationMs = durationMs;
    }
}
