namespace Nornis.Application.Ai;

/// <summary>
/// Structured model output that cannot be parsed or validated — a model-behavior
/// problem, not an HTTP one. Callers with a parse-retry loop retry these (sampling
/// variance means the next attempt usually parses); exhausted retries classify as
/// ParseFailure.
/// </summary>
public class AiParseException : Exception
{
    /// <summary>
    /// Tokens the provider billed for the attempt that failed to parse, when the caller had
    /// them in scope. Unparseable output is still paid output: recording zero here made the
    /// daily budget guard undercount exactly when a model was misbehaving and every attempt
    /// was being retried — the moment spend is roughest and the guard matters most.
    /// Null when the failure happened before any usage was reported.
    /// </summary>
    public AiUsage? Usage { get; init; }

    public AiParseException(string message)
        : base(message)
    {
    }

    public AiParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
