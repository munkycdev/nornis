using Nornis.Application.Ai;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Binds every call to one <see cref="FakeChatClient"/> on Nornis's own key, the way
/// <see cref="AiCallBinder"/> does before any user has a key of their own (feature 39). Records
/// each bind, which is how a test reads the scope and task a client named.
/// </summary>
public sealed class FakeAiCallBinder : IAiCallBinder
{
    private readonly FakeChatClient _client;
    private readonly List<(AiCallScope Scope, AiTask Task, string NornisModel)> _binds = [];

    public FakeAiCallBinder(FakeChatClient client) => _client = client;

    public IReadOnlyList<(AiCallScope Scope, AiTask Task, string NornisModel)> Binds => _binds;

    public Task<AiBinding> BindAsync(AiCallScope scope, AiTask task, string nornisModel, CancellationToken ct)
    {
        _binds.Add((scope, task, nornisModel));
        return Task.FromResult(new AiBinding(
            _client, OpenAiProviderTraits.Instance, new AiFunding(AiProvider.AzureOpenAi, nornisModel, AiFundedBy.Nornis)));
    }
}
