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

    /// <summary>Providers a catalog entry may name: each needs a client and traits. Gemini joins
    /// later in Phase C.</summary>
    public static readonly IReadOnlySet<AiProvider> CatalogProviders = new HashSet<AiProvider> { AiProvider.OpenAi, AiProvider.Anthropic };

    public AiCatalogModel? Find(string id) =>
        Catalog.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

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
                _ when model.InputPerMillionUsd <= 0 || model.OutputPerMillionUsd <= 0 => $"'{model.Id}' has no price",
                _ when model.CachedInputPerMillionUsd is < 0 => $"'{model.Id}' has a negative cached price",
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

    public AiModelPrice Price => new(InputPerMillionUsd, CachedInputPerMillionUsd, OutputPerMillionUsd);
}

/// <summary>USD per million tokens. A null cached price means cached input costs full price.</summary>
public sealed record AiModelPrice(decimal InputPerMillionUsd, decimal? CachedInputPerMillionUsd, decimal OutputPerMillionUsd)
{
    public decimal Cost(int inputTokens, int? cachedInputTokens, int outputTokens)
    {
        var cached = Math.Clamp(cachedInputTokens ?? 0, 0, inputTokens);
        var cachedRate = CachedInputPerMillionUsd ?? InputPerMillionUsd;
        return ((inputTokens - cached) * InputPerMillionUsd + cached * cachedRate + outputTokens * OutputPerMillionUsd)
            / 1_000_000m;
    }
}
