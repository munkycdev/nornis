using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 8: Processing Status Guards on Delete
///
/// For any source with ProcessingStatus of Queued or Processing, any delete request
/// (regardless of whether the actor is the creator or a GM) should be rejected with an
/// error indicating the source cannot be deleted while being processed.
///
/// **Validates: Requirements 4.3**
/// </summary>
[TestFixture]
[Category("Feature: campaign-sources, Property 8: Processing Status Guards on Delete")]
public class SourceProcessingStatusGuardsOnDeleteTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(DeleteProcessingGuardArbitraries)],
        MaxTest = 100)]
    [Description("Feature: campaign-sources, Property 8: Processing Status Guards on Delete")]
    public void CreatorCannotDeleteSource_WhenProcessingStatusIsQueuedOrProcessing(DeleteProcessingGuardScenario scenario)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, queueClient);

        sourceRepo.CreateAsync(scenario.ExistingSource, CancellationToken.None).GetAwaiter().GetResult();

        // Act — creator attempts to delete
        var result = service.DeleteAsync(
            scenario.ExistingSource.Id,
            scenario.ExistingSource.CampaignId,
            scenario.ExistingSource.CreatedByUserId,
            CampaignRole.Player,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should be rejected with invalid_status error
        Assert.That(result.IsSuccess, Is.False,
            $"Delete should be rejected when source is in {scenario.ExistingSource.ProcessingStatus} status.");
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_status"),
            "Error code should be 'invalid_status' for processing status guard.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409),
            "Status code should be 409 Conflict for processing status guard.");
        Assert.That(result.Error!.Message,
            Is.EqualTo($"Source cannot be deleted while in {scenario.ExistingSource.ProcessingStatus} status."),
            "Error message should indicate the current processing status.");

        // Assert — source should still exist in the repository
        var stillExists = sourceRepo.Sources.Any(s => s.Id == scenario.ExistingSource.Id);
        Assert.That(stillExists, Is.True,
            "Source should still exist after rejected delete attempt.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(DeleteProcessingGuardArbitraries)],
        MaxTest = 100)]
    [Description("Feature: campaign-sources, Property 8: Processing Status Guards on Delete")]
    public void GMCannotDeleteSource_WhenProcessingStatusIsQueuedOrProcessing(DeleteProcessingGuardScenario scenario)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, queueClient);

        sourceRepo.CreateAsync(scenario.ExistingSource, CancellationToken.None).GetAwaiter().GetResult();

        // Act — GM attempts to delete (GMs are normally authorized, but processing status blocks it)
        var result = service.DeleteAsync(
            scenario.ExistingSource.Id,
            scenario.ExistingSource.CampaignId,
            scenario.GmUserId,
            CampaignRole.GM,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should be rejected with invalid_status error
        Assert.That(result.IsSuccess, Is.False,
            $"GM delete should be rejected when source is in {scenario.ExistingSource.ProcessingStatus} status.");
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_status"),
            "Error code should be 'invalid_status' for processing status guard.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409),
            "Status code should be 409 Conflict for processing status guard.");
        Assert.That(result.Error!.Message,
            Is.EqualTo($"Source cannot be deleted while in {scenario.ExistingSource.ProcessingStatus} status."),
            "Error message should indicate the current processing status.");

        // Assert — source should still exist in the repository
        var stillExists = sourceRepo.Sources.Any(s => s.Id == scenario.ExistingSource.Id);
        Assert.That(stillExists, Is.True,
            "Source should still exist after rejected GM delete attempt.");
    }
}

/// <summary>
/// Input model for delete processing status guard scenarios.
/// Represents a source in Queued or Processing status with associated user ids.
/// </summary>
public record DeleteProcessingGuardScenario(
    Source ExistingSource,
    Guid GmUserId);

/// <summary>
/// Custom FsCheck arbitraries for processing status guards on delete tests.
/// Generates sources with ProcessingStatus randomly chosen from {Queued, Processing}.
/// </summary>
public class DeleteProcessingGuardArbitraries
{
    private static readonly char[] TitleChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'".ToCharArray();

    public static Arbitrary<DeleteProcessingGuardScenario> DeleteProcessingGuardScenarios()
    {
        var validTitleGen =
            from length in Gen.Choose(1, 100)
            from chars in Gen.Elements(TitleChars).ArrayOf(length)
            let title = new string(chars).Trim()
            where !string.IsNullOrWhiteSpace(title) && title.Length >= 1 && title.Length <= 200
            select title;

        var sourceTypeGen = Gen.Elements(
            SourceType.SessionNote,
            SourceType.JournalEntry,
            SourceType.Transcript,
            SourceType.Upload,
            SourceType.Image,
            SourceType.HandwrittenNotes,
            SourceType.WebLink,
            SourceType.GMNote,
            SourceType.ImportedNote);

        var visibilityGen = Gen.Elements(
            VisibilityScope.Private,
            VisibilityScope.GMOnly,
            VisibilityScope.PartyVisible);

        // Only generate statuses that block deletion
        var blockedStatusGen = Gen.Elements(
            SourceProcessingStatus.Queued,
            SourceProcessingStatus.Processing);

        var gen =
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from creatorUserId in ArbMap.Default.GeneratorFor<Guid>()
            from gmUserId in ArbMap.Default.GeneratorFor<Guid>()
            where gmUserId != creatorUserId
            from title in validTitleGen
            from sourceType in sourceTypeGen
            from visibility in visibilityGen
            from processingStatus in blockedStatusGen
            from daysAgo in Gen.Choose(0, 365)
            select new DeleteProcessingGuardScenario(
                new Source
                {
                    Id = Guid.NewGuid(),
                    CampaignId = campaignId,
                    Type = sourceType,
                    Title = title,
                    Body = "Tavrin's Journal — The Silver Key found in Captain Voss's quarters",
                    Uri = null,
                    OccurredAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
                    CreatedByUserId = creatorUserId,
                    Visibility = visibility,
                    ProcessingStatus = processingStatus
                },
                gmUserId);

        return gen.ToArbitrary();
    }
}
