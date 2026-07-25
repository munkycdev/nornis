using Nornis.Domain.Entities;

namespace Nornis.Domain.Models;

/// <summary>
/// The rows a world export selected, loaded as plain untracked entity lists (no
/// navigations). Lists for categories that were not selected stay empty.
/// </summary>
public class WorldExportData
{
    public IReadOnlyList<WorldMember> Members { get; init; } = [];

    public IReadOnlyList<Campaign> Campaigns { get; init; } = [];

    public IReadOnlyList<CampaignCharacter> CampaignCharacters { get; init; } = [];

    public IReadOnlyList<StorylineCampaign> StorylineCampaigns { get; init; } = [];

    public IReadOnlyList<Character> Characters { get; init; } = [];

    public IReadOnlyList<Source> Sources { get; init; } = [];

    public IReadOnlyList<SourceExtraction> SourceExtractions { get; init; } = [];

    public IReadOnlyList<SourceReference> SourceReferences { get; init; } = [];

    public IReadOnlyList<SourceAttachment> Attachments { get; init; } = [];

    public IReadOnlyList<Artifact> Artifacts { get; init; } = [];

    public IReadOnlyList<ArtifactFact> ArtifactFacts { get; init; } = [];

    public IReadOnlyList<ArtifactRelationship> ArtifactRelationships { get; init; } = [];

    public IReadOnlyList<MapPlacemark> MapPlacemarks { get; init; } = [];

    public IReadOnlyList<LibraryDocument> LibraryDocuments { get; init; } = [];

    public IReadOnlyList<ReviewBatch> ReviewBatches { get; init; } = [];

    public IReadOnlyList<ReviewProposal> ReviewProposals { get; init; } = [];

    public IReadOnlyList<HealthAssessment> HealthAssessments { get; init; } = [];

    public IReadOnlyList<ContinuityFinding> ContinuityFindings { get; init; } = [];

    public IReadOnlyList<AiUsageRecord> AiUsageRecords { get; init; } = [];
}
