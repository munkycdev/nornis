using Nornis.Domain.Entities;

namespace Nornis.Application.Ai;

/// <summary>A model a provider lists for a key: its wire name, and the name to show.</summary>
public sealed record AiListedModel(string Id, string Name);

/// <summary>
/// The chat models a key's provider lists for it (feature 39, Dave's picker decision), offered on
/// Profile after the catalog's as "not tested by Nornis". Listing models costs nothing. Answers
/// are kept for an hour per key version, so Profile doesn't ask the provider on every visit, and
/// a replaced key is asked afresh.
/// </summary>
public interface IAiModelLister
{
    /// <summary>The provider's chat models for <paramref name="key"/>, or null when the provider
    /// couldn't be asked (it refused the key, timed out, or the provider has no list).</summary>
    Task<IReadOnlyList<AiListedModel>?> ListChatModelsAsync(UserAiKey key, string secret, CancellationToken ct);
}
