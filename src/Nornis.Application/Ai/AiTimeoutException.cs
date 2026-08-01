namespace Nornis.Application.Ai;

/// <summary>
/// An AI call that exceeded its configured timeout. Always classified transient — a
/// timeout says nothing about the request's validity.
/// </summary>
public class AiTimeoutException : Exception
{
    public AiTimeoutException(string message)
        : base(message)
    {
    }

    public AiTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
