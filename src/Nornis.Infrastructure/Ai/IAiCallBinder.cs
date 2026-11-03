using Nornis.Application.Ai;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Binds one AI call to the client that makes it (feature 39): asks
/// <see cref="IAiFundingService"/> who pays and on which model, then hands back that provider's
/// client and traits. Called on every call by <see cref="AiCallExecutor"/>, so no client holds a
/// key or a model of its own.
/// </summary>
public interface IAiCallBinder
{
    /// <param name="needsImages">The call sends images, so only a model that reads them will do.</param>
    /// <exception cref="AiRefusalException">Nothing can pay, or the paying key can't be used.</exception>
    Task<AiBinding> BindAsync(AiCallScope scope, AiTask task, string nornisModel, bool needsImages, CancellationToken ct);

    /// <summary>
    /// How a bound call ended: <paramref name="failure"/> is null on success, otherwise the failure
    /// as translated. Feeds the provider's health (<see cref="IAiOutcomeMonitor"/>), which never
    /// counts a refusal, and marks a key its provider refused, so the next call is refused by the
    /// funding rule instead of being sent.
    /// </summary>
    Task ReportAsync(AiBinding binding, Exception? failure, CancellationToken ct);
}

public sealed class AiCallBinder : IAiCallBinder
{
    private readonly IAiFundingService _funding;
    private readonly NornisChatClients _nornis;
    private readonly UserKeyChatClients _userKeys;
    private readonly IAiKeyRepository _keys;
    private readonly IAiKeyProtector _protector;
    private readonly IAiOutcomeMonitor _outcomes;
    private readonly TimeProvider _time;

    public AiCallBinder(
        IAiFundingService funding,
        NornisChatClients nornis,
        UserKeyChatClients userKeys,
        IAiKeyRepository keys,
        IAiKeyProtector protector,
        IAiOutcomeMonitor outcomes,
        TimeProvider time)
    {
        _outcomes = outcomes;
        _funding = funding;
        _nornis = nornis;
        _userKeys = userKeys;
        _keys = keys;
        _protector = protector;
        _time = time;
    }

    public async Task<AiBinding> BindAsync(
        AiCallScope scope, AiTask task, string nornisModel, bool needsImages, CancellationToken ct)
    {
        var funding = await _funding.ResolveAsync(scope, task, nornisModel, needsImages, ct);

        if (funding.FundedBy != AiFundedBy.Key)
        {
            return funding.Provider == AiProvider.AzureOpenAi
                ? new AiBinding(_nornis.For(funding.Model), OpenAiProviderTraits.Instance, funding)
                : throw new InvalidOperationException($"Nornis pays only on its own Azure deployments, not {funding.Provider}.");
        }

        // Removed between the funding decision and now: the same answer a refused key gets.
        var key = await _keys.GetAsync(funding.AiKeyId!.Value, ct)
            ?? throw new AiKeyRejectedException(funding.Provider);

        // Decrypted only when the cache has no client for this key version. A lost ring reads as
        // a rejected key (design: AiKeyProtector): the key is marked so it isn't tried again, and
        // nothing is cached, because the factory throws.
        Microsoft.Extensions.AI.IChatClient client;
        try
        {
            client = _userKeys.For(key, funding.Model, () =>
                _protector.Unprotect(key.ProtectedKey) ?? throw new UnreadableKeyException());
        }
        catch (UnreadableKeyException)
        {
            await _keys.MarkCheckFailedAsync(key.Id, key.UpdatedAt, _time.GetUtcNow(), ct);
            throw new AiKeyRejectedException(key.Provider);
        }

        return new AiBinding(client, TraitsFor(key.Provider), funding, key.UpdatedAt);
    }

    public Task ReportAsync(AiBinding binding, Exception? failure, CancellationToken ct)
    {
        if (failure is not AiRefusalException)
        {
            _outcomes.Record(binding.Funding.Provider, failure is null, _time.GetUtcNow());
            return Task.CompletedTask;
        }

        return failure is AiKeyRejectedException && binding is { Funding.AiKeyId: { } keyId, KeyVersion: { } version }
            ? _keys.MarkCheckFailedAsync(keyId, version, _time.GetUtcNow(), ct)
            : Task.CompletedTask;
    }

    private sealed class UnreadableKeyException : Exception;

    private static IAiProviderTraits TraitsFor(AiProvider provider) => provider switch
    {
        AiProvider.OpenAi or AiProvider.OpenAiCompatible => OpenAiProviderTraits.Instance,
        _ => throw new NotSupportedException($"No client is built for {provider} keys yet (feature 39 Phase C)."),
    };
}
