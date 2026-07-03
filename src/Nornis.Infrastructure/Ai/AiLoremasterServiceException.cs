namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Thrown when the AI service returns a 5xx server error.
/// The LoremasterService classifies this as a transient error (503).
/// </summary>
public class AiLoremasterServiceException : Exception
{
    public int HttpStatus { get; }
    public int DurationMs { get; }

    public AiLoremasterServiceException(string message, int httpStatus, int durationMs)
        : base(message)
    {
        HttpStatus = httpStatus;
        DurationMs = durationMs;
    }

    public AiLoremasterServiceException(string message, int httpStatus, int durationMs, Exception innerException)
        : base(message, innerException)
    {
        HttpStatus = httpStatus;
        DurationMs = durationMs;
    }
}
