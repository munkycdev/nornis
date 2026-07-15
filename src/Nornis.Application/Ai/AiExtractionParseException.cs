namespace Nornis.Application.Ai;

/// <summary>
/// Thrown when the AI structured output response cannot be parsed or validated.
/// The ExtractionService retries these through the parse-retry loop (sampling variance
/// means the next attempt usually parses); exhausted retries classify as ParseFailure.
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
