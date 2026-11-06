using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Ai;

/// <summary>Feature 40's one rule for whether a Prep turn may search the web.</summary>
[TestFixture]
public class WebResearchAvailabilityTests
{
    private static readonly AiModelsOptions Models = new()
    {
        Catalog =
        [
            new AiCatalogModel
            {
                Id = "openai/searcher", Provider = AiProvider.OpenAi, Model = "searcher", Tasks = [AiTask.Prep],
                InputPerMillionUsd = 1m, OutputPerMillionUsd = 1m, SupportsWebSearch = true, WebSearchPerThousandUsd = 10m,
            },
            new AiCatalogModel
            {
                Id = "openai/quiet", Provider = AiProvider.OpenAi, Model = "quiet", Tasks = [AiTask.Prep],
                InputPerMillionUsd = 1m, OutputPerMillionUsd = 1m,
            },
        ],
    };

    private static AiFunding Key(AiProvider provider, string? modelId) =>
        new(provider, "m", AiFundedBy.Key, Guid.NewGuid(), Guid.NewGuid(), modelId);

    [TestCase(AiFundedBy.Nornis)]
    [TestCase(AiFundedBy.Trial)]
    public void NornisOrTheTrial_NeverSearches(AiFundedBy fundedBy) =>
        Assert.That(WebResearchAvailability.For(new AiFunding(AiProvider.OpenAi, "m", fundedBy, ModelId: "openai/searcher"), Models),
            Is.EqualTo(WebResearchAvailabilityKind.NeedsOwnKey), "searches are billed per call, so only a key pays for them");

    [Test]
    public void AnOpenAiCompatibleKey_CantSearch() =>
        Assert.That(WebResearchAvailability.For(Key(AiProvider.OpenAiCompatible, null), Models),
            Is.EqualTo(WebResearchAvailabilityKind.NotOnCompatible));

    [Test]
    public void ACatalogModel_SearchesOnlyWhenMarked()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WebResearchAvailability.For(Key(AiProvider.OpenAi, "openai/searcher"), Models),
                Is.EqualTo(WebResearchAvailabilityKind.Available));
            Assert.That(WebResearchAvailability.For(Key(AiProvider.OpenAi, "openai/quiet"), Models),
                Is.EqualTo(WebResearchAvailabilityKind.ModelCantSearch));
        });
    }

    [Test]
    public void AnUntestedModel_MayTry() =>
        Assert.That(WebResearchAvailability.For(Key(AiProvider.Anthropic, "untested:Anthropic/claude-next"), Models),
            Is.EqualTo(WebResearchAvailabilityKind.Available), "the provider's refusal, not Nornis, says it can't");

    [Test]
    public void EveryUnavailableKind_HasItsReason_AndAvailableHasNone()
    {
        foreach (var kind in Enum.GetValues<WebResearchAvailabilityKind>())
        {
            var reason = WebResearchAvailability.Reason(kind);
            if (kind == WebResearchAvailabilityKind.Available)
            {
                Assert.That(reason, Is.Null);
            }
            else
            {
                Assert.That(reason, Is.Not.Null.And.Not.Empty, kind.ToString());
            }
        }
    }
}
