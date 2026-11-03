using Nornis.Application.Ai;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Binds every call to one <see cref="FakeChatClient"/>, by default on Nornis's own key, the way
/// <see cref="AiCallBinder"/> does when nobody has a key (feature 39). Records each bind and each
/// reported key rejection, which is how a test reads what a client named.
/// </summary>
public sealed class FakeAiCallBinder : IAiCallBinder
{
    private readonly FakeChatClient _client;
    private readonly Func<string, AiFunding> _funding;
    private readonly List<(AiCallScope Scope, AiTask Task, string NornisModel, bool NeedsImages)> _binds = [];
    private readonly List<AiBinding> _rejections = [];

    public FakeAiCallBinder(FakeChatClient client, Func<string, AiFunding>? funding = null)
    {
        _client = client;
        _funding = funding ?? (model => new AiFunding(AiProvider.AzureOpenAi, model, AiFundedBy.Nornis));
    }

    public IReadOnlyList<(AiCallScope Scope, AiTask Task, string NornisModel, bool NeedsImages)> Binds => _binds;

    public IReadOnlyList<AiBinding> Rejections => _rejections;

    public Task<AiBinding> BindAsync(AiCallScope scope, AiTask task, string nornisModel, bool needsImages, CancellationToken ct)
    {
        _binds.Add((scope, task, nornisModel, needsImages));
        var funding = _funding(nornisModel);
        return Task.FromResult(new AiBinding(_client, OpenAiProviderTraits.Instance, funding,
            funding.FundedBy == AiFundedBy.Key ? DateTimeOffset.UnixEpoch : null));
    }

    public Task ReportKeyRejectedAsync(AiBinding binding, CancellationToken ct)
    {
        _rejections.Add(binding);
        return Task.CompletedTask;
    }
}
