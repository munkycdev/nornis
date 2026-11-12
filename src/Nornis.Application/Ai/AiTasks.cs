using Nornis.Domain.Enums;

namespace Nornis.Application.Ai;

public static class AiTasks
{
    /// <summary>
    /// Work only a key holder's own key can run (feature 38): Nornis has no deployment for it, so
    /// neither Nornis nor the trial pays, and an OpenAI-compatible (chat) endpoint can't serve it.
    /// </summary>
    public static bool OnlyKeysRun(AiTask task) => IsImageTask(task);

    /// <summary>
    /// Work an image model does, not a chat model (features 38, 45). The catalog keeps the two
    /// apart: an image model serves only these, and these only image models.
    /// </summary>
    public static bool IsImageTask(AiTask task) => task is AiTask.CoverArt or AiTask.ArtRefinement;

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
        AiOperationType.CheckIn => AiTask.Ask,

        AiOperationType.SourceExtraction => AiTask.Extraction,
        AiOperationType.SourceExtractionRepair => AiTask.Extraction,
        AiOperationType.ArtifactSummary => AiTask.Extraction,
        AiOperationType.RelationshipBackfill => AiTask.Extraction,
        AiOperationType.ImageReading => AiTask.Extraction,
        AiOperationType.MapExtraction => AiTask.Extraction,

        AiOperationType.AdventureDrafting => AiTask.Prep,
        AiOperationType.AdventureConversation => AiTask.Prep,
        AiOperationType.CoverArtBrief => AiTask.Prep,

        AiOperationType.CoverArt => AiTask.CoverArt,
        AiOperationType.KeyArt => AiTask.CoverArt,

        AiOperationType.CoverArtRefinement => AiTask.ArtRefinement,
        AiOperationType.KeyArtRefinement => AiTask.ArtRefinement,

        AiOperationType.WorldNaming => AiTask.Chores,
        AiOperationType.ConvergenceNarration => AiTask.Chores,

        AiOperationType.Embedding => null,

        _ => throw new ArgumentOutOfRangeException(
            nameof(operation), operation, "Every AI operation must belong to an AI task."),
    };
}
