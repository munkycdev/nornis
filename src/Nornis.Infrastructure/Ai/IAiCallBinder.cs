using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Binds one AI call to the client that makes it (feature 39): asks
/// <see cref="IAiFundingService"/> who pays and on which model, then hands back that provider's
/// client and traits. Called on every call by <see cref="AiCallExecutor"/>, so no client holds a
/// key or a model of its own.
/// </summary>
public interface IAiCallBinder
{
    Task<AiBinding> BindAsync(AiCallScope scope, AiTask task, string nornisModel, CancellationToken ct);
}

public sealed class AiCallBinder : IAiCallBinder
{
    private readonly IAiFundingService _funding;
    private readonly NornisChatClients _nornis;

    public AiCallBinder(IAiFundingService funding, NornisChatClients nornis)
    {
        _funding = funding;
        _nornis = nornis;
    }

    public async Task<AiBinding> BindAsync(AiCallScope scope, AiTask task, string nornisModel, CancellationToken ct)
    {
        var funding = await _funding.ResolveAsync(scope, task, nornisModel, ct);

        return funding.Provider switch
        {
            Domain.Enums.AiProvider.AzureOpenAi =>
                new AiBinding(_nornis.For(funding.Model), OpenAiProviderTraits.Instance, funding),
            _ => throw new NotSupportedException(
                $"No client is built for {funding.Provider} yet; user keys arrive in feature 39 Phase B."),
        };
    }
}
