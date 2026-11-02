namespace Nornis.Application.Bibliotaph;

/// <summary>
/// How a call to Bibliotaph can fail, already turned into what Nornis does about it. Callers
/// switch on this, never on a status code.
/// </summary>
public enum BibliotaphFailure
{
    /// <summary>The user has no connection.</summary>
    NoConnection,

    /// <summary>Bibliotaph or Auth0 refused the grant. The connection is now Needs reconnect.</summary>
    NotConnected,

    /// <summary>The library is opening; try again in a moment.</summary>
    Opening,

    /// <summary>The book is in a folder kept from AI.</summary>
    KeptFromAi,

    /// <summary>Not in the world's collection, or the world is gone there.</summary>
    NotFound,

    /// <summary>Bibliotaph refused the request as malformed, or with a code Nornis doesn't know.</summary>
    Rejected,

    /// <summary>A timeout, a network failure or a server error.</summary>
    Unreachable,
}

public sealed class BibliotaphResult<T>
{
    private BibliotaphResult(T? value, BibliotaphFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public BibliotaphFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static BibliotaphResult<T> Success(T value) => new(value, null);

    public static BibliotaphResult<T> Fail(BibliotaphFailure failure) => new(default, failure);
}

/// <summary>For calls whose answer has no body.</summary>
public readonly record struct BibliotaphAccepted;
