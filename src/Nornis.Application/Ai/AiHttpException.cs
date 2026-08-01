namespace Nornis.Application.Ai;

/// <summary>
/// An AI call that failed at the HTTP layer — the third leg of the taxonomy beside
/// <see cref="AiTimeoutException"/> and <see cref="AiParseException"/>. Carries the
/// typed status so <see cref="TransientFailureClassifier"/> never has to read prose;
/// null status means the failure happened below HTTP (socket, DNS, SDK internals).
/// </summary>
public class AiHttpException : Exception
{
    public int? StatusCode { get; }

    public AiHttpException(string message, int? statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
