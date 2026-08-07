using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Enums;

/// <summary>
/// The names are the contract, which is why these tests assert names and no enum in the domain
/// declares a numeric value. Every persisted enum is configured <c>HasConversion&lt;string&gt;()</c>
/// and every API contract carries the enum as a string, so the ordinal is stored nowhere and
/// crosses no wire — reordering members is free, and <em>renaming</em> one is the change that
/// silently orphans existing rows. Pinning numbers would advertise the opposite.
/// </summary>
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
            "Image", "HandwrittenNotes", "WebLink", "GMNote", "ImportedNote",
            "SessionAudio", "FanFiction", "Map", "Reveal");
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
            "AddFact", "UpdateFact", "AddRelationship", "UpdateRelationship", "AddPlacemark");
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
            "SourceExtraction", "ArtifactSummary", "AskLoremaster", "SourceExtractionRepair", "ContinuityAudit", "StorylineRetrospective", "Embedding", "RelationshipBackfill", "HandwritingTranscription", "ImageReading", "MapExtraction", "ContinuityFix", "WorldNaming",
            "WorldDigest", "ConvergenceNarration");
    }















    [Test]
    public void ContinuityFindingCategory_HasExpectedValues()
    {
        AssertEnumHasExactValues<ContinuityFindingCategory>(
            "Contradiction", "DanglingThread", "StaleStoryline", "TimelineConflict",
            "SummaryDrift", "DuplicateArtifact");
    }

    [Test]
    public void InviteStatus_HasExpectedValues()
    {
        AssertEnumHasExactValues<InviteStatus>("Active", "Revoked", "Expired", "Exhausted");
    }
}
