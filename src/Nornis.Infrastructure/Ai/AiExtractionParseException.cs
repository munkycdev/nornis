namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Thrown when the AI structured output response cannot be parsed or validated.
/// The ExtractionService classifies this as a non-transient error (ParseFailure).
/// </summary>
public class AiExtractionParseException : Exception
{
    public AiExtractionParseException(string message)
        : base(message)
    {
    }

    public AiExtractionParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
