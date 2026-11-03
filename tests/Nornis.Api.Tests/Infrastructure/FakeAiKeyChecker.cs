using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>Answers every key check with <see cref="Outcome"/>, and remembers what it was asked,
/// so a test can save a key without calling a provider.</summary>
public sealed class FakeAiKeyChecker : IAiKeyChecker
{
    public AiKeyCheckOutcome Outcome { get; set; } = AiKeyCheckOutcome.Accepted;

    public List<(AiProvider Provider, string Key, Uri? BaseUrl)> Checks { get; } = [];

    public Task<AiKeyCheckOutcome> CheckAsync(AiProvider provider, string key, Uri? baseUrl, CancellationToken ct)
    {
        Checks.Add((provider, key, baseUrl));
        return Task.FromResult(Outcome);
    }
}
