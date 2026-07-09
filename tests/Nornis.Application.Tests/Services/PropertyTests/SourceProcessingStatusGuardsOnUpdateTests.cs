using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 7: Processing Status Guards on Update
///
/// For any source with ProcessingStatus of Queued, Processing, or Processed,
/// any update request (regardless of whether the actor is the creator or a GM)
/// should be rejected with an error indicating the source cannot be modified in its current processing state.
///
/// **Validates: Requirements 3.3**
/// </summary>
[TestFixture]
[Category("Feature: world-sources, Property 7: Processing Status Guards on Update")]
public class SourceProcessingStatusGuardsOnUpdateTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ProcessingStatusGuardUpdateArbitraries)],
        MaxTest = 100)]
    [Description("Feature: world-sources, Property 7: Processing Status Guards on Update")]
    public void Creator_CannotUpdateSource_WhenProcessingStatusBlocked(ProcessingStatusUpdateScenario scenario)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, new InMemoryCampaignRepository(), queueClient);

        sourceRepo.CreateAsync(scenario.ExistingSource, CancellationToken.None).GetAwaiter().GetResult();

        // Actor is the creator
        var command = new UpdateSourceCommand(
            scenario.ExistingSource.Id,
            scenario.ExistingSource.WorldId,
            scenario.ExistingSource.CreatedByUserId,
            WorldRole.Player,
            Title: "Updated Title — Session 5");

        // Act
        var result = service.UpdateAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should be rejected with invalid_status error
        Assert.That(result.IsSuccess, Is.False,
            $"Update should be rejected when source is in {scenario.ExistingSource.ProcessingStatus} status.");
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_status"),
            "Error code should be 'invalid_status' for processing status guard.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409),
            "Status code should be 409 Conflict for processing status guard.");
        Assert.That(result.Error!.Message,
            Is.EqualTo($"Source cannot be modified while in {scenario.ExistingSource.ProcessingStatus} status."),
            "Error message should indicate the current processing status.");

        // Assert — source should remain unchanged in repository
        var unchanged = sourceRepo.Sources.First(s => s.Id == scenario.ExistingSource.Id);
        Assert.That(unchanged.Title, Is.EqualTo(scenario.ExistingSource.Title),
            "Source title should remain unchanged after rejected update.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ProcessingStatusGuardUpdateArbitraries)],
        MaxTest = 100)]
    [Description("Feature: world-sources, Property 7: Processing Status Guards on Update")]
    public void GM_CannotUpdateSource_WhenProcessingStatusBlocked(ProcessingStatusUpdateScenario scenario)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, new InMemoryCampaignRepository(), queueClient);

        sourceRepo.CreateAsync(scenario.ExistingSource, CancellationToken.None).GetAwaiter().GetResult();

        // Actor is a GM (different user from creator)
        var gmUserId = Guid.NewGuid();
        var command = new UpdateSourceCommand(
            scenario.ExistingSource.Id,
            scenario.ExistingSource.WorldId,
            gmUserId,
            WorldRole.GM,
            Title: "GM Updated Title — Black Harbor");

        // Act
        var result = service.UpdateAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should be rejected with invalid_status error even for GMs
        Assert.That(result.IsSuccess, Is.False,
            $"GM update should also be rejected when source is in {scenario.ExistingSource.ProcessingStatus} status.");
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_status"),
            "Error code should be 'invalid_status' for GM update on blocked status.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409),
            "Status code should be 409 Conflict for GM update on blocked status.");
        Assert.That(result.Error!.Message,
            Is.EqualTo($"Source cannot be modified while in {scenario.ExistingSource.ProcessingStatus} status."),
            "Error message should indicate the current processing status.");

        // Assert — source should remain unchanged in repository
        var unchanged = sourceRepo.Sources.First(s => s.Id == scenario.ExistingSource.Id);
        Assert.That(unchanged.Title, Is.EqualTo(scenario.ExistingSource.Title),
            "Source title should remain unchanged after rejected GM update.");
    }
}

/// <summary>
/// Input model for processing status guard on update scenarios.
/// Represents a source in a blocked processing state (Queued, Processing, or Processed).
/// </summary>
public record ProcessingStatusUpdateScenario(Source ExistingSource);

/// <summary>
/// Custom FsCheck arbitraries for processing status guards on update tests.
/// Generates sources with ProcessingStatus randomly chosen from {Queued, Processing, Processed}.
/// </summary>
public class ProcessingStatusGuardUpdateArbitraries
{
    private static readonly char[] TitleChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'".ToCharArray();

    public static Arbitrary<ProcessingStatusUpdateScenario> ProcessingStatusUpdateScenarios()
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

        // Only statuses that should block updates
        var blockedStatusGen = Gen.Elements(
            SourceProcessingStatus.Queued,
            SourceProcessingStatus.Processing,
            SourceProcessingStatus.Processed);

        var gen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from creatorUserId in ArbMap.Default.GeneratorFor<Guid>()
            from title in validTitleGen
            from sourceType in sourceTypeGen
            from visibility in visibilityGen
            from blockedStatus in blockedStatusGen
            from daysAgo in Gen.Choose(0, 365)
            select new ProcessingStatusUpdateScenario(
                new Source
                {
                    Id = Guid.NewGuid(),
                    WorldId = worldId,
                    Type = sourceType,
                    Title = title,
                    Body = "Captain Voss denied knowing about the missing caravan",
                    Uri = null,
                    OccurredAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
                    CreatedByUserId = creatorUserId,
                    Visibility = visibility,
                    ProcessingStatus = blockedStatus
                });

        return gen.ToArbitrary();
    }
}
