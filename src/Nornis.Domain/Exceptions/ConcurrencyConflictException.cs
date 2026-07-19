namespace Nornis.Domain.Exceptions;

/// <summary>
/// Thrown by a repository when an optimistic-concurrency token (RowVersion) check fails
/// because the row changed since it was read. Lets application services react to write
/// races without depending on the persistence provider's exception types.
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message) : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
