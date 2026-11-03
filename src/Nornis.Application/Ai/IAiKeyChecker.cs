using Nornis.Domain.Enums;

namespace Nornis.Application.Ai;

/// <summary>What a provider said when asked to list models with a key.</summary>
public enum AiKeyCheckOutcome
{
    /// <summary>The provider answered with the key: it can be saved.</summary>
    Accepted,

    /// <summary>The provider refused the key (401 or 403).</summary>
    Rejected,

    /// <summary>The endpoint answered but has no model list there (404): most likely a wrong base URL.</summary>
    NotFound,

    /// <summary>No usable answer: a timeout, a network failure, a 5xx, or an endpoint Nornis won't call.</summary>
    Unreachable,
}

/// <summary>
/// Checks a key before it is saved (feature 39) by listing models with it, which costs nothing.
/// Ten seconds at most. The API limits how often a user may ask, so a stolen session can't use
/// Nornis to test keys.
/// </summary>
public interface IAiKeyChecker
{
    /// <param name="baseUrl">The endpoint, for <see cref="AiProvider.OpenAiCompatible"/> only.</param>
    Task<AiKeyCheckOutcome> CheckAsync(AiProvider provider, string key, Uri? baseUrl, CancellationToken ct);
}
