using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>Which budget an AI operation's spend counts against.</summary>
public enum AiSpendPool
{
    /// <summary>The world's daily budget, which extraction and Ask depend on.</summary>
    DailyWorld,

    /// <summary>The world's monthly Prep budget (feature 29). Never touches the daily one.</summary>
    PrepMonthly,
}

/// <summary>
/// The one place that says which pool each operation spends from. The daily guard sums only
/// the operations in <see cref="AiSpendPool.DailyWorld"/>, and the Prep guard only those in
/// <see cref="AiSpendPool.PrepMonthly"/>, so an operation can't be counted twice or not at all.
/// The switch has no default arm: a new <see cref="AiOperationType"/> fails
/// <c>AiSpendPoolsTests</c> until someone decides where its spend belongs.
/// </summary>
public static class AiSpendPools
{
    public static AiSpendPool PoolOf(AiOperationType operation) => operation switch
    {
        AiOperationType.SourceExtraction => AiSpendPool.DailyWorld,
        AiOperationType.ArtifactSummary => AiSpendPool.DailyWorld,
        AiOperationType.AskLoremaster => AiSpendPool.DailyWorld,
        AiOperationType.SourceExtractionRepair => AiSpendPool.DailyWorld,
        AiOperationType.ContinuityAudit => AiSpendPool.DailyWorld,
        AiOperationType.StorylineRetrospective => AiSpendPool.DailyWorld,
        AiOperationType.Embedding => AiSpendPool.DailyWorld,
        AiOperationType.RelationshipBackfill => AiSpendPool.DailyWorld,
        AiOperationType.HandwritingTranscription => AiSpendPool.DailyWorld,
        AiOperationType.ImageReading => AiSpendPool.DailyWorld,
        AiOperationType.MapExtraction => AiSpendPool.DailyWorld,
        AiOperationType.ContinuityFix => AiSpendPool.DailyWorld,
        AiOperationType.WorldNaming => AiSpendPool.DailyWorld,
        AiOperationType.WorldDigest => AiSpendPool.DailyWorld,
        AiOperationType.CampaignRecap => AiSpendPool.DailyWorld,
        AiOperationType.ConvergenceNarration => AiSpendPool.DailyWorld,
        AiOperationType.AdventureDrafting => AiSpendPool.PrepMonthly,
        AiOperationType.AdventureConversation => AiSpendPool.PrepMonthly,
        AiOperationType.CoverArtBrief => AiSpendPool.PrepMonthly,
        AiOperationType.CoverArt => AiSpendPool.PrepMonthly,
        AiOperationType.KeyArt => AiSpendPool.PrepMonthly,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation,
            "This operation has no spend pool. Add it to AiSpendPools.PoolOf."),
    };

    /// <summary>Every operation whose spend counts against <paramref name="pool"/>.</summary>
    public static IReadOnlyList<AiOperationType> OperationsIn(AiSpendPool pool) =>
        Enum.GetValues<AiOperationType>().Where(op => PoolOf(op) == pool).ToList();
}
