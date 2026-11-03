using Nornis.Application.Ai;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Binds every call to one <see cref="FakeChatClient"/>, by default on Nornis's own key, the way
/// <see cref="AiCallBinder"/> does when nobody has a key (feature 39). Records each bind and each
/// reported outcome, which is how a test reads what a client named and how its call ended.
/// </summary>
public sealed class FakeAiCallBinder : IAiCallBinder
{
    private readonly FakeChatClient _client;
    private readonly Func<string, AiFunding> _funding;
    private readonly List<(AiCallScope Scope, AiTask Task, string NornisModel, bool NeedsImages)> _binds = [];
    private readonly List<(AiBinding Binding, Exception? Failure)> _reports = [];

    public FakeAiCallBinder(FakeChatClient client, Func<string, AiFunding>? funding = null)
    {
        _client = client;
        _funding = funding ?? (model => new AiFunding(AiProvider.AzureOpenAi, model, AiFundedBy.Nornis));
    }

    public IReadOnlyList<(AiCallScope Scope, AiTask Task, string NornisModel, bool NeedsImages)> Binds => _binds;

    /// <summary>Every outcome the executor reported: null for a success.</summary>
    public IReadOnlyList<(AiBinding Binding, Exception? Failure)> Reports => _reports;

    /// <summary>The reported outcomes in which the provider refused the key.</summary>
    public IReadOnlyList<AiBinding> Rejections =>
        _reports.Where(r => r.Failure is AiKeyRejectedException).Select(r => r.Binding).ToList();

    public Task<AiBinding> BindAsync(AiCallScope scope, AiTask task, string nornisModel, bool needsImages, CancellationToken ct)
    {
        _binds.Add((scope, task, nornisModel, needsImages));
        var funding = _funding(nornisModel);
        return Task.FromResult(new AiBinding(_client, OpenAiProviderTraits.Instance, funding,
            funding.FundedBy == AiFundedBy.Key ? DateTimeOffset.UnixEpoch : null));
    }

    public Task ReportAsync(AiBinding binding, Exception? failure, CancellationToken ct)
    {
        _reports.Add((binding, failure));
        return Task.CompletedTask;
    }
}
