using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Application.Configuration;

/// <summary>
/// The models users can run on their own keys, and the trial (feature 39). The catalog is
/// curated: a model with no entry here can't be picked, and every entry carries its prices, so a
/// key-funded call is never priced at $0 by a typo. Nornis's own Azure deployments are not in it;
/// they are priced by the <c>ModelPricing</c> sections, as before.
/// </summary>
public sealed class AiModelsOptions
{
    public const string SectionName = "AiModels";

    /// <summary>
    /// What pays when the key holder has no key for a task. <see cref="AiFallback.Nornis"/> until
    /// the cutover (Phase D), so nothing changes for anyone who hasn't added a key;
    /// <see cref="AiFallback.Trial"/> after it.
    /// </summary>
    public AiFallback Fallback { get; set; } = AiFallback.Nornis;

    /// <summary>A new user's one-time trial, in USD. Zero turns the trial off.</summary>
    public decimal TrialAllowanceUsd { get; set; } = 2.00m;

    /// <summary>What all trials together may spend in a calendar month (UTC), in USD.</summary>
    public decimal TrialMonthlyCeilingUsd { get; set; } = 25.00m;

    /// <summary>In default order: a task's default is the first entry its key holder has a key for.</summary>
    public List<AiCatalogModel> Catalog { get; set; } = [];

    /// <summary>Providers a catalog entry may name: each needs a client and traits.</summary>
    public static readonly IReadOnlySet<AiProvider> CatalogProviders =
        new HashSet<AiProvider> { AiProvider.OpenAi, AiProvider.Anthropic, AiProvider.Gemini };

    public AiCatalogModel? Find(string id) =>
        Catalog.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// What a model the catalog doesn't list is priced at: the provider's dearest catalog chat
    /// model, so an estimate errs high rather than low. Null when the catalog has none of the
    /// provider's, and then its untested models aren't offered.
    /// </summary>
    /// <remarks>Its search price is the provider's dearest search price (feature 40), whichever
    /// model carries it, so an untested model's searches err high too; null when none of the
    /// provider's catalog models searches.</remarks>
    public AiModelPrice? EstimateFor(AiProvider provider)
    {
        var chat = Catalog.Where(m => m.Provider == provider && !m.IsImageModel).ToList();
        var dearest = chat
            .OrderByDescending(m => m.OutputPerMillionUsd)
            .ThenByDescending(m => m.InputPerMillionUsd)
            .FirstOrDefault();

        return dearest is null
            ? null
            : dearest.Price with
            {
                WebSearchPerThousandUsd = chat.Where(m => m.SupportsWebSearch).Max(m => m.WebSearchPerThousandUsd),
            };
    }

    /// <summary>Null when the section can run; otherwise what is wrong with it, for startup to refuse.</summary>
    public string? StartupProblem()
    {
        if (TrialAllowanceUsd < 0 || TrialMonthlyCeilingUsd < 0)
        {
            return "AiModels: the trial allowance and monthly ceiling can't be negative.";
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in Catalog)
        {
            var problem = model switch
            {
                { Id: null or "" } => "an entry has no Id",
                _ when !ids.Add(model.Id) => $"'{model.Id}' is listed twice",
                { Model: null or "" } => $"'{model.Id}' has no Model",
                _ when !CatalogProviders.Contains(model.Provider) => $"'{model.Id}' names {model.Provider}, which has no client yet",
                { Tasks.Count: 0 } => $"'{model.Id}' serves no task",
                _ when model.IsImageModel && !model.Tasks.All(AiTasks.IsImageTask) =>
                    $"'{model.Id}' makes images, so it can serve only CoverArt and ArtRefinement",
                _ when !model.IsImageModel && model.Tasks.Any(AiTasks.IsImageTask) =>
                    $"'{model.Id}' serves ArtRefinement, which only an image model can, so it must serve CoverArt too",
                _ when model.InputPerMillionUsd <= 0 || model.OutputPerMillionUsd <= 0 => $"'{model.Id}' has no price",
                _ when model.CachedInputPerMillionUsd is < 0 => $"'{model.Id}' has a negative cached price",
                { SupportsWebSearch: true, WebSearchPerThousandUsd: null or <= 0 } =>
                    $"'{model.Id}' searches the web but has no search price",
                { SupportsWebSearch: false, WebSearchPerThousandUsd: not null } =>
                    $"'{model.Id}' has a search price but doesn't search the web",
                { SupportsWebSearch: true } when !model.Tasks.Contains(AiTask.Prep) =>
                    $"'{model.Id}' searches the web, which only Prep asks for, but doesn't serve Prep",
                _ => null,
            };

            if (problem is not null)
            {
                return $"AiModels:Catalog: {problem}.";
            }
        }

        return null;
    }
}

public enum AiFallback
{
    /// <summary>Nornis's own key pays, as before feature 39.</summary>
    Nornis,

    /// <summary>The key holder's trial pays while it lasts; then the call is refused.</summary>
    Trial
}

/// <summary>One model users can pick (feature 39).</summary>
public sealed class AiCatalogModel
{
    /// <summary>Stable id, <c>{provider}/{model}</c>: what a user's choice and the ledger store.</summary>
    public string Id { get; set; } = string.Empty;

    public AiProvider Provider { get; set; }

    /// <summary>The provider's model name, as sent on the wire.</summary>
    public string Model { get; set; } = string.Empty;

    public List<AiTask> Tasks { get; set; } = [];

    public bool SupportsImages { get; set; }

    public decimal InputPerMillionUsd { get; set; }

    /// <summary>Null when the provider gives cached input no discount.</summary>
    public decimal? CachedInputPerMillionUsd { get; set; }

    public decimal OutputPerMillionUsd { get; set; }

    /// <summary>
    /// Nornis has tested this model with its provider's own web search (feature 40), so Prep's
    /// Web research switch is offered on it. False means it can't search; the price below is its
    /// own field, never a stand-in for this one.
    /// </summary>
    public bool SupportsWebSearch { get; set; }

    /// <summary>USD per 1,000 searches. Required, and above zero, exactly when <see cref="SupportsWebSearch"/>.</summary>
    public decimal? WebSearchPerThousandUsd { get; set; }

    /// <summary>
    /// An image model: it makes pictures (<see cref="AiTask.CoverArt"/>), and may also refine them.
    /// Validation keeps image models and chat models from sharing a task.
    /// </summary>
    public bool IsImageModel => Tasks.Contains(AiTask.CoverArt);

    public AiModelPrice Price => new(InputPerMillionUsd, CachedInputPerMillionUsd, OutputPerMillionUsd, WebSearchPerThousandUsd);
}

/// <summary>
/// USD per million tokens, and per 1,000 web searches (feature 40). A null cached price means
/// cached input costs full price. A null search price means the model doesn't search, so a call
/// that reports searches on it prices them at zero rather than inventing a rate.
/// </summary>
public sealed record AiModelPrice(
    decimal InputPerMillionUsd, decimal? CachedInputPerMillionUsd, decimal OutputPerMillionUsd,
    decimal? WebSearchPerThousandUsd = null)
{
    /// <param name="webSearches">Searches the provider ran for the call; null when it reported none.</param>
    public decimal Cost(int inputTokens, int? cachedInputTokens, int outputTokens, int? webSearches = null)
    {
        var cached = Math.Clamp(cachedInputTokens ?? 0, 0, inputTokens);
        var cachedRate = CachedInputPerMillionUsd ?? InputPerMillionUsd;
        var tokens = ((inputTokens - cached) * InputPerMillionUsd + cached * cachedRate + outputTokens * OutputPerMillionUsd)
            / 1_000_000m;
        var searches = Math.Max(webSearches ?? 0, 0) * (WebSearchPerThousandUsd ?? 0m) / 1_000m;
        return tokens + searches;
    }
}
