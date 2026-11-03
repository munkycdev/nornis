using Nornis.Domain.Enums;

namespace Nornis.Application.Ai;

/// <summary>
/// A kind of AI work that gets its own model choice (feature 39). Every
/// <see cref="AiOperationType"/> belongs to exactly one, through <see cref="AiTasks.For"/>.
/// </summary>
public enum AiTask
{
    /// <summary>The Loremaster and the other answers a GM reads.</summary>
    Ask,

    /// <summary>Everything that proposes canon, and reading images for it.</summary>
    Extraction,

    /// <summary>Prep drafting and the Adventure's side conversation.</summary>
    Prep,

    /// <summary>Short background text: world names, convergence notes.</summary>
    Chores
}

public static class AiTasks
{
    /// <summary>
    /// The task an operation's model choice comes from. Null for <see cref="AiOperationType.Embedding"/>,
    /// which Nornis pays for and nobody chooses. The switch has no default arm on purpose: a new
    /// operation fails <c>AiTasksTests</c> until it is placed here.
    /// </summary>
    public static AiTask? For(AiOperationType operation) => operation switch
    {
        AiOperationType.AskLoremaster => AiTask.Ask,
        AiOperationType.ContinuityAudit => AiTask.Ask,
        AiOperationType.ContinuityFix => AiTask.Ask,
        AiOperationType.StorylineRetrospective => AiTask.Ask,
        AiOperationType.WorldDigest => AiTask.Ask,
        AiOperationType.CampaignRecap => AiTask.Ask,
        AiOperationType.HandwritingTranscription => AiTask.Ask,

        AiOperationType.SourceExtraction => AiTask.Extraction,
        AiOperationType.SourceExtractionRepair => AiTask.Extraction,
        AiOperationType.ArtifactSummary => AiTask.Extraction,
        AiOperationType.RelationshipBackfill => AiTask.Extraction,
        AiOperationType.ImageReading => AiTask.Extraction,
        AiOperationType.MapExtraction => AiTask.Extraction,

        AiOperationType.AdventureDrafting => AiTask.Prep,
        AiOperationType.AdventureConversation => AiTask.Prep,

        AiOperationType.WorldNaming => AiTask.Chores,
        AiOperationType.ConvergenceNarration => AiTask.Chores,

        AiOperationType.Embedding => null,

        _ => throw new ArgumentOutOfRangeException(
            nameof(operation), operation, "Every AI operation must belong to an AI task."),
    };
}
