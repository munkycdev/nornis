using Nornis.Application.Errors;
using Nornis.Domain.Enums;

namespace Nornis.Application.Ai;

/// <summary>
/// An AI call that was not made, or was refused, because of who pays for it (feature 39): no key,
/// a rejected key, or a provider account out of credit. Each carries the code the Web renders as
/// its call to action and the <see cref="ErrorCategories"/> value a failed source records. They
/// are <see cref="AiHttpException"/>s with a 4xx status, so every existing catch and
/// <see cref="TransientFailureClassifier"/> already treats them as permanent: retrying the same
/// call can't help until the GM changes a key.
/// </summary>
public abstract class AiRefusalException : AiHttpException
{
    protected AiRefusalException(string message, int statusCode, string code, string category,
        AiProvider? provider, Exception? innerException = null)
        : base(message, statusCode, innerException)
    {
        Code = code;
        Category = category;
        Provider = provider;
    }

    /// <summary>The error code an API response carries: <c>ai_key_required</c> and so on.</summary>
    public string Code { get; }

    /// <summary>The <see cref="ErrorCategories"/> value for a source this failed.</summary>
    public string Category { get; }

    /// <summary>The provider whose key or account is the problem, when one is.</summary>
    public AiProvider? Provider { get; }

    /// <summary>
    /// The API's answer: 409 with <see cref="Code"/>, which the Web renders as its call to action.
    /// Not the exception's own status: that one describes the provider's reply, and a 401 here
    /// would read to the browser as the user's session having expired.
    /// </summary>
    public AppError ToAppError() => new(409, Code, UserMessage);

    /// <summary>What a person reads; the exception's message is for the log.</summary>
    protected abstract string UserMessage { get; }
}

/// <summary>Nothing can pay: the key holder has no key for the task and no trial to spend.</summary>
public sealed class AiKeyRequiredException : AiRefusalException
{
    public AiKeyRequiredException(AiTask task, string reason)
        : base($"An AI key is needed for {task}: {reason}", 402, "ai_key_required", ErrorCategories.AiKeyRequired, null)
    {
        Task = task;
    }

    public AiTask Task { get; }

    protected override string UserMessage =>
        "This world's AI needs a key. The world's creator can add one under Your account, AI.";
}

/// <summary>The provider refused the key (on its check or on a call). It is not spent again until
/// the key holder saves it anew.</summary>
public sealed class AiKeyRejectedException : AiRefusalException
{
    public AiKeyRejectedException(AiProvider provider, Exception? innerException = null)
        : base($"The {provider} key was rejected by the provider.", 401, "ai_key_rejected",
            ErrorCategories.AiKeyRejected, provider, innerException)
    {
    }

    protected override string UserMessage =>
        $"{AiProviderNames.Display(Provider!.Value)} refused the key this world uses. The world's creator can replace it under Your account, AI.";
}

/// <summary>The key works but its provider account has no credit left.</summary>
public sealed class AiCreditExhaustedException : AiRefusalException
{
    public AiCreditExhaustedException(AiProvider provider, Exception? innerException = null)
        : base($"The {provider} account has run out of credit.", 402, "ai_credit_exhausted",
            ErrorCategories.AiCreditExhausted, provider, innerException)
    {
    }

    protected override string UserMessage =>
        $"The {AiProviderNames.Display(Provider!.Value)} account behind this world's key is out of credit. Add credit there, then try again.";
}
