namespace Nornis.Application.Ai;

/// <summary>
/// Structured model output that cannot be parsed or validated — a model-behavior
/// problem, not an HTTP one. Callers with a parse-retry loop retry these (sampling
/// variance means the next attempt usually parses); exhausted retries classify as
/// ParseFailure.
/// </summary>
public class AiParseException : Exception
{
    public AiParseException(string message)
        : base(message)
    {
    }

    public AiParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
