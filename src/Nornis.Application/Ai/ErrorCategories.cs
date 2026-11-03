namespace Nornis.Application.Ai;

public static class ErrorCategories
{
    public const string TransientError = "TransientError";
    public const string SourceNotFound = "SourceNotFound";
    public const string EmptySourceBody = "EmptySourceBody";
    public const string ValidationFailure = "ValidationFailure";
    public const string AiCallFailure = "AiCallFailure";
    public const string ParseFailure = "ParseFailure";
    public const string Timeout = "Timeout";

    /// <summary>
    /// The world's AI spend guard refused the call. Five pipelines produced this category
    /// as a bare literal before it moved here, and it is the one category a caller outside
    /// the queue reads back to decide an HTTP status — see
    /// <c>SourceTranscriptionService</c>, whose 429 has to name the same condition the
    /// budget guard's own error does.
    /// </summary>
    public const string BudgetExceeded = "BudgetExceeded";

    /// <summary>Feature 39: the world's creator has no AI key for the task and no trial left.
    /// Saving a key queues the sources that failed with this again.</summary>
    public const string AiKeyRequired = "AiKeyRequired";

    /// <summary>Feature 39: the provider refused the key that would pay.</summary>
    public const string AiKeyRejected = "AiKeyRejected";

    /// <summary>Feature 39: the key works, but its provider account is out of credit.</summary>
    public const string AiCreditExhausted = "AiCreditExhausted";
}
