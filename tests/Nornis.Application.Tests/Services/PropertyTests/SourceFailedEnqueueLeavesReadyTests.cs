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
/// Property 11: Failed Enqueue Leaves Source at Ready
///
/// For any source in Draft status where the extraction queue client fails, marking the source as ready
/// should leave the ProcessingStatus at Ready (not Queued) and return an error indicating the enqueue
/// operation failed.
///
/// **Validates: Requirements 7.3**
/// </summary>
[TestFixture]
[Category("Feature: campaign-sources, Property 11: Failed Enqueue Leaves Source at Ready")]
public class SourceFailedEnqueueLeavesReadyTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(FailedEnqueueArbitraries)],
        MaxTest = 100)]
    [Description("Feature: campaign-sources, Property 11: Failed Enqueue Leaves Source at Ready")]
    public void MarkReadyAsync_WhenQueueFails_LeavesSourceAtReady_ReturnsError(FailedEnqueueScenario scenario)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        queueClient.ConfigureToFail(true);

        var service = new SourceService(sourceRepo, memberRepo, queueClient);

        var source = new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = scenario.CampaignId,
            Type = scenario.SourceType,
            Title = scenario.Title,
            Body = scenario.Body,
            Visibility = scenario.Visibility,
            ProcessingStatus = SourceProcessingStatus.Draft,
            CreatedByUserId = scenario.CreatorUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        sourceRepo.CreateAsync(source, CancellationToken.None).GetAwaiter().GetResult();

        var command = new MarkSourceReadyCommand(
            source.Id,
            scenario.CampaignId,
            scenario.CreatorUserId,
            CampaignRole.GM);

        // Act
        var result = service.MarkReadyAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert - result is failure with error code "enqueue_failed" and status code 502
        Assert.That(result.IsSuccess, Is.False,
            "MarkReadyAsync should fail when the extraction queue client fails.");
        Assert.That(result.Error!.Code, Is.EqualTo("enqueue_failed"),
            "Error code should be 'enqueue_failed' when queue operation fails.");
        Assert.That(result.Error.StatusCode, Is.EqualTo(502),
            "Status code should be 502 (Bad Gateway) when queue operation fails.");

        // Assert - source ProcessingStatus is Ready (not Queued, not Draft)
        var updatedSource = sourceRepo.Sources.First(s => s.Id == source.Id);
        Assert.That(updatedSource.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Ready),
            "Source should remain at Ready status when enqueue fails (not Queued, not Draft).");

        // Assert - no messages were sent (SentMessages is empty)
        Assert.That(queueClient.SentMessages, Is.Empty,
            "No messages should be sent when the queue client is configured to fail.");
    }
}

/// <summary>
/// Scenario for testing failed enqueue behavior during mark-ready.
/// </summary>
public record FailedEnqueueScenario(
    Guid CampaignId,
    Guid CreatorUserId,
    string Title,
    string? Body,
    SourceType SourceType,
    VisibilityScope Visibility);

/// <summary>
/// Custom FsCheck arbitraries for failed enqueue property tests.
/// </summary>
public class FailedEnqueueArbitraries
{
    private static readonly char[] TitleChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'.,:;!?".ToCharArray();

    private static readonly char[] BodyChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!?-\n\r\t".ToCharArray();

    public static Arbitrary<FailedEnqueueScenario> FailedEnqueueScenarios()
    {
        var validTitleGen =
            from length in Gen.Choose(1, 200)
            from chars in Gen.Elements(TitleChars).ArrayOf(length)
            let title = new string(chars).Trim()
            where !string.IsNullOrWhiteSpace(title) && title.Length >= 1 && title.Length <= 200
            select title;

        var validSourceTypeGen = Gen.Elements(
            SourceType.SessionNote,
            SourceType.JournalEntry,
            SourceType.Transcript,
            SourceType.Upload,
            SourceType.Image,
            SourceType.HandwrittenNotes,
            SourceType.WebLink,
            SourceType.GMNote,
            SourceType.ImportedNote);

        var validVisibilityGen = Gen.Elements(
            VisibilityScope.Private,
            VisibilityScope.GMOnly,
            VisibilityScope.PartyVisible);

        var optionalBodyGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            from length in Gen.Choose(1, 500)
            from chars in Gen.Elements(BodyChars).ArrayOf(length)
            select (string?)new string(chars));

        var gen =
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from creatorUserId in ArbMap.Default.GeneratorFor<Guid>()
            from title in validTitleGen
            from body in optionalBodyGen
            from sourceType in validSourceTypeGen
            from visibility in validVisibilityGen
            select new FailedEnqueueScenario(campaignId, creatorUserId, title, body, sourceType, visibility);

        return gen.ToArbitrary();
    }
}
