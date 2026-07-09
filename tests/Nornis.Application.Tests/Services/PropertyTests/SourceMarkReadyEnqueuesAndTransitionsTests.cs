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
/// Property 10: Mark Ready Enqueues and Transitions to Queued
///
/// For any source in Draft status where the acting user is the creator or a GM, marking the source as ready
/// should: place an extraction message on the queue containing the Source Id and World Id, and transition
/// the ProcessingStatus to Queued.
///
/// **Validates: Requirements 6.1, 7.1, 7.2**
/// </summary>
[TestFixture]
[Category("Feature: world-sources, Property 10: Mark Ready Enqueues and Transitions to Queued")]
public class SourceMarkReadyEnqueuesAndTransitionsTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(MarkReadyEnqueueArbitraries)],
        MaxTest = 100)]
    [Description("Feature: world-sources, Property 10: Mark Ready Enqueues and Transitions to Queued")]
    public void MarkReady_DraftSource_EnqueuesMessageAndTransitionsToQueued(MarkReadyScenario scenario)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, queueClient);

        var source = new Source
        {
            Id = scenario.SourceId,
            WorldId = scenario.WorldId,
            Type = scenario.SourceType,
            Title = scenario.Title,
            Body = scenario.Body,
            Visibility = scenario.Visibility,
            ProcessingStatus = SourceProcessingStatus.Draft,
            CreatedByUserId = scenario.CreatorUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        sourceRepo.CreateAsync(source, CancellationToken.None).GetAwaiter().GetResult();

        // Determine acting user: either the creator or a GM (different user)
        var actingUserId = scenario.ActAsCreator ? scenario.CreatorUserId : scenario.GmUserId;
        var actingRole = scenario.ActAsCreator ? WorldRole.GM : WorldRole.GM;
        // If acting as creator, they could be a Player or GM — but must be creator OR GM
        if (scenario.ActAsCreator)
        {
            actingRole = scenario.CreatorRole;
        }

        var command = new MarkSourceReadyCommand(
            scenario.SourceId,
            scenario.WorldId,
            actingUserId,
            actingRole);

        // Act
        var result = service.MarkReadyAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert - operation should succeed
        Assert.That(result.IsSuccess, Is.True,
            "MarkReady should succeed for a Draft source when called by creator or GM.");

        // Assert - source ProcessingStatus is Queued
        var updatedSource = sourceRepo.Sources.First(s => s.Id == scenario.SourceId);
        Assert.That(updatedSource.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued),
            "After successful MarkReady, source ProcessingStatus must be Queued.");

        // Assert - exactly one extraction message was sent
        Assert.That(queueClient.SentMessages.Count, Is.EqualTo(1),
            "Exactly one extraction message should be placed on the queue.");

        // Assert - message contains correct SourceId and WorldId
        var sentMessage = queueClient.SentMessages[0];
        Assert.That(sentMessage.SourceId, Is.EqualTo(scenario.SourceId),
            "Extraction message must contain the correct Source Id.");
        Assert.That(sentMessage.WorldId, Is.EqualTo(scenario.WorldId),
            "Extraction message must contain the correct World Id.");
    }
}

/// <summary>
/// Scenario for testing MarkReady enqueue behavior on Draft sources.
/// </summary>
public record MarkReadyScenario(
    Guid SourceId,
    Guid WorldId,
    Guid CreatorUserId,
    Guid GmUserId,
    bool ActAsCreator,
    WorldRole CreatorRole,
    SourceType SourceType,
    string Title,
    string? Body,
    VisibilityScope Visibility);

/// <summary>
/// Custom FsCheck arbitraries for mark-ready enqueue property tests.
/// </summary>
public class MarkReadyEnqueueArbitraries
{
    private static readonly char[] TitleChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'.,:;!?".ToCharArray();

    public static Arbitrary<MarkReadyScenario> MarkReadyScenarios()
    {
        var validTitleGen =
            from length in Gen.Choose(1, 200)
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

        // Creator can be GM or Player (both are allowed to mark ready on their own sources)
        var creatorRoleGen = Gen.Elements(WorldRole.GM, WorldRole.Player);

        var optionalBodyGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>("We questioned Captain Voss in Black Harbor."));

        var gen =
            from sourceId in ArbMap.Default.GeneratorFor<Guid>()
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from creatorUserId in ArbMap.Default.GeneratorFor<Guid>()
            from gmUserId in ArbMap.Default.GeneratorFor<Guid>()
            where creatorUserId != gmUserId
            from actAsCreator in ArbMap.Default.GeneratorFor<bool>()
            from creatorRole in creatorRoleGen
            from sourceType in sourceTypeGen
            from title in validTitleGen
            from body in optionalBodyGen
            from visibility in visibilityGen
            select new MarkReadyScenario(
                sourceId,
                worldId,
                creatorUserId,
                gmUserId,
                actAsCreator,
                creatorRole,
                sourceType,
                title,
                body,
                visibility);

        return gen.ToArbitrary();
    }
}
