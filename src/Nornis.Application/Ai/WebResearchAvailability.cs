using Nornis.Application.Configuration;
using Nornis.Domain.Enums;

namespace Nornis.Application.Ai;

/// <summary>Whether a Prep turn can search the web, and if not, why (feature 40).</summary>
public enum WebResearchAvailabilityKind
{
    Available,

    /// <summary>Nornis or the trial would pay. Searches are billed per call, so only a key runs them.</summary>
    NeedsOwnKey,

    /// <summary>An OpenAI-compatible endpoint: Nornis can't know it has a search tool.</summary>
    NotOnCompatible,

    /// <summary>A catalog model Nornis hasn't marked <see cref="AiCatalogModel.SupportsWebSearch"/>.</summary>
    ModelCantSearch,
}

/// <summary>
/// The one place that decides whether web research is offered (feature 40). The side
/// conversation asks it to render the switch and again before a research turn starts, and the
/// binder asserts it, so no caller can search on money that can't pay for it.
/// </summary>
public static class WebResearchAvailability
{
    public static WebResearchAvailabilityKind For(AiFunding funding, AiModelsOptions models)
    {
        if (funding.FundedBy != AiFundedBy.Key)
        {
            return WebResearchAvailabilityKind.NeedsOwnKey;
        }

        if (funding.Provider == AiProvider.OpenAiCompatible)
        {
            return WebResearchAvailabilityKind.NotOnCompatible;
        }

        // A model the catalog doesn't list is the GM's own pick: it may try, and a provider that
        // refuses the tool fails the turn where they can see it (Requirement 5).
        return funding.ModelId is { } id && models.Find(id) is { SupportsWebSearch: false }
            ? WebResearchAvailabilityKind.ModelCantSearch
            : WebResearchAvailabilityKind.Available;
    }

    /// <summary>What the switch says when it is off for <paramref name="kind"/>; null when available.</summary>
    public static string? Reason(WebResearchAvailabilityKind kind) => kind switch
    {
        WebResearchAvailabilityKind.Available => null,
        WebResearchAvailabilityKind.NeedsOwnKey => "Web research runs on your own AI key. Add one under Your account, AI.",
        WebResearchAvailabilityKind.NotOnCompatible => "Web research isn't available on OpenAI-compatible endpoints.",
        WebResearchAvailabilityKind.ModelCantSearch => "Your Prep model can't search the web. Pick another under Your account, AI.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "An availability with no reason."),
    };
}
