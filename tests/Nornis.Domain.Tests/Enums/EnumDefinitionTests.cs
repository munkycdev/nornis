using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Enums;

[TestFixture]
public class EnumDefinitionTests
{
    private static void AssertEnumHasExactValues<TEnum>(params string[] expectedNames) where TEnum : struct, Enum
    {
        var actualNames = Enum.GetNames<TEnum>();

        Assert.That(actualNames, Is.EquivalentTo(expectedNames),
            $"{typeof(TEnum).Name} should contain exactly the expected values.");
    }

    [Test]
    public void WorldRole_HasExpectedValues()
    {
        AssertEnumHasExactValues<WorldRole>("GM", "Player", "Observer");
    }

    [Test]
    public void SourceType_HasExpectedValues()
    {
        AssertEnumHasExactValues<SourceType>(
            "SessionNote", "JournalEntry", "Transcript", "Upload",
            "Image", "HandwrittenNotes", "WebLink", "GMNote", "ImportedNote");
    }

    [Test]
    public void SourceProcessingStatus_HasExpectedValues()
    {
        AssertEnumHasExactValues<SourceProcessingStatus>(
            "Draft", "Ready", "Queued", "Processing", "Processed", "Failed");
    }

    [Test]
    public void ArtifactType_HasExpectedValues()
    {
        AssertEnumHasExactValues<ArtifactType>(
            "Character", "Location", "Item", "Faction",
            "Event", "Storyline", "Concept", "Document");
    }

    [Test]
    public void ArtifactStatus_HasExpectedValues()
    {
        AssertEnumHasExactValues<ArtifactStatus>("Active", "Dormant", "Resolved", "Archived");
    }

    [Test]
    public void TruthState_HasExpectedValues()
    {
        AssertEnumHasExactValues<TruthState>(
            "Confirmed", "Likely", "Rumor", "Disputed", "False", "Hidden");
    }

    [Test]
    public void VisibilityScope_HasExpectedValues()
    {
        AssertEnumHasExactValues<VisibilityScope>("Private", "GMOnly", "PartyVisible");
    }

    [Test]
    public void ReviewBatchStatus_HasExpectedValues()
    {
        AssertEnumHasExactValues<ReviewBatchStatus>(
            "Pending", "InReview", "Completed", "Canceled", "Failed");
    }

    [Test]
    public void ReviewProposalStatus_HasExpectedValues()
    {
        AssertEnumHasExactValues<ReviewProposalStatus>("Pending", "Accepted", "Rejected", "Edited");
    }

    [Test]
    public void ReviewChangeType_HasExpectedValues()
    {
        AssertEnumHasExactValues<ReviewChangeType>(
            "CreateArtifact", "UpdateArtifact", "MergeArtifact",
            "AddFact", "UpdateFact", "AddRelationship", "UpdateRelationship");
    }

    [Test]
    public void ReviewTargetType_HasExpectedValues()
    {
        AssertEnumHasExactValues<ReviewTargetType>("Artifact", "ArtifactFact", "ArtifactRelationship");
    }

    [Test]
    public void SourceExtractionType_HasExpectedValues()
    {
        AssertEnumHasExactValues<SourceExtractionType>(
            "Manual", "OCR", "VisionSummary", "Transcription", "WebPageText");
    }

    [Test]
    public void SourceReferenceTargetType_HasExpectedValues()
    {
        AssertEnumHasExactValues<SourceReferenceTargetType>(
            "Artifact", "ArtifactFact", "ArtifactRelationship", "ReviewProposal");
    }

    [Test]
    public void AiOperationType_HasExpectedValues()
    {
        AssertEnumHasExactValues<AiOperationType>(
            "SourceExtraction", "ArtifactSummary", "AskLoremaster", "SourceExtractionRepair", "ContinuityAudit", "StorylineRetrospective", "Embedding");
    }

    [Test]
    public void ConversationRole_HasExpectedValues()
    {
        AssertEnumHasExactValues<ConversationRole>("User", "Assistant");
    }

    [Test]
    public void WorldRole_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<WorldRole>(), Has.Length.EqualTo(3));
    }

    [Test]
    public void SourceType_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<SourceType>(), Has.Length.EqualTo(9));
    }

    [Test]
    public void SourceProcessingStatus_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<SourceProcessingStatus>(), Has.Length.EqualTo(6));
    }

    [Test]
    public void ArtifactType_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<ArtifactType>(), Has.Length.EqualTo(8));
    }

    [Test]
    public void ArtifactStatus_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<ArtifactStatus>(), Has.Length.EqualTo(4));
    }

    [Test]
    public void TruthState_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<TruthState>(), Has.Length.EqualTo(6));
    }

    [Test]
    public void VisibilityScope_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<VisibilityScope>(), Has.Length.EqualTo(3));
    }

    [Test]
    public void ReviewBatchStatus_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<ReviewBatchStatus>(), Has.Length.EqualTo(5));
    }

    [Test]
    public void ReviewProposalStatus_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<ReviewProposalStatus>(), Has.Length.EqualTo(4));
    }

    [Test]
    public void ReviewChangeType_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<ReviewChangeType>(), Has.Length.EqualTo(7));
    }

    [Test]
    public void ReviewTargetType_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<ReviewTargetType>(), Has.Length.EqualTo(3));
    }

    [Test]
    public void SourceExtractionType_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<SourceExtractionType>(), Has.Length.EqualTo(5));
    }

    [Test]
    public void SourceReferenceTargetType_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<SourceReferenceTargetType>(), Has.Length.EqualTo(4));
    }

    [Test]
    public void AiOperationType_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<AiOperationType>(), Has.Length.EqualTo(7));
    }

    [Test]
    public void ConversationRole_HasNoUnexpectedValues()
    {
        Assert.That(Enum.GetNames<ConversationRole>(), Has.Length.EqualTo(2));
    }

    [Test]
    public void AllEnums_AreInExpectedNamespace()
    {
        var enumTypes = typeof(WorldRole).Assembly
            .GetTypes()
            .Where(t => t.IsEnum && t.Namespace == "Nornis.Domain.Enums")
            .ToList();

        // 15 original + ContinuityFindingCategory/Severity/Status (AI-assessed Continuity
        // Health) + CampaignStatus (worlds-and-campaigns) + LibraryDocumentKind/Status (Library).
        Assert.That(enumTypes, Has.Count.EqualTo(21),
            "Expected exactly 21 enums in Nornis.Domain.Enums namespace.");
    }
}
