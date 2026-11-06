using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Fakes;

/// <summary>The funding rule, fixed: every call is funded as <see cref="Funding"/> says, or refused with <see cref="Refusal"/>.</summary>
public sealed class FakeAiFundingService : IAiFundingService
{
    /// <summary>Nornis's own key by default, as a world with no key of its creator's runs.</summary>
    public AiFunding Funding { get; set; } = new(AiProvider.AzureOpenAi, "nornis-ask", AiFundedBy.Nornis);

    public AiRefusalException? Refusal { get; set; }

    public int Calls { get; private set; }

    public Task<AiFunding> ResolveAsync(AiCallScope scope, AiTask task, string? nornisModel, bool needsImages, CancellationToken ct)
    {
        Calls++;
        return Refusal is null ? Task.FromResult(Funding) : Task.FromException<AiFunding>(Refusal);
    }
}
