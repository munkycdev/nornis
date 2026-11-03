namespace Nornis.Api.Contracts.Responses;

/// <summary>One of the user's AI keys (feature 39). The key itself is never in any response; the
/// last four characters say which one it is.</summary>
public record AiKeyResponse(
    string Provider,
    string Label,
    string LastFour,
    string? BaseUrl,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset CheckedAt,
    bool CheckFailed,
    IReadOnlyList<AiCompatibleModelResponse> Models);

public record AiCompatibleModelResponse(
    string Task,
    string ModelName,
    bool SupportsImages,
    decimal InputPerMillionUsd,
    decimal OutputPerMillionUsd);

/// <summary>The saved key, and how many sources that had failed for want of one were queued again.</summary>
public record SaveAiKeyResponse(AiKeyResponse Key, int SourcesQueued);

/// <summary>One task's models: what can be picked, the user's pick, and what runs it with no pick.</summary>
public record AiTaskModelsResponse(
    string Task,
    IReadOnlyList<AiModelOptionResponse> Options,
    string? ChosenId,
    string? DefaultId);

/// <param name="Tested">False for an OpenAI-compatible model, which Nornis has never run.</param>
public record AiModelOptionResponse(
    string Id,
    string Name,
    string Provider,
    bool SupportsImages,
    bool HasKey,
    bool Tested);

/// <summary>The user's trial: whether Nornis pays through trials at all, and what is left.</summary>
public record AiTrialResponse(
    bool InUse,
    decimal AllowanceUsd,
    decimal SpentUsd,
    decimal RemainingUsd,
    bool Available);

/// <summary>A world's AI funding, for its GMs: whether some of its AI work runs on the creator's
/// trial, and what is left of it.</summary>
public record WorldAiFundingResponse(bool Trial, decimal? TrialRemainingUsd, bool CreatorIsYou);

/// <summary>Costs by what paid (feature 39). <c>Kind</c> is Nornis, Trial or Key; <c>PaidBy</c>
/// names it for a person: a key's label, or "Trial", or "Nornis".</summary>
public record FundingCostResponse(string Kind, string PaidBy, CostSummaryResponse Summary);
