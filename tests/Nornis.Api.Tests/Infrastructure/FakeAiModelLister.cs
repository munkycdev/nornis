using Nornis.Application.Ai;
using Nornis.Domain.Entities;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>Answers every model list with <see cref="Models"/>, so Profile's "More from your key"
/// can be tested without calling a provider.</summary>
public sealed class FakeAiModelLister : IAiModelLister
{
    public IReadOnlyList<AiListedModel>? Models { get; set; } = [];

    public Task<IReadOnlyList<AiListedModel>?> ListChatModelsAsync(UserAiKey key, string secret, CancellationToken ct) =>
        Task.FromResult(Models);
}
