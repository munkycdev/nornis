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
/// Property 4: Only Creator or GM Can Mutate a Source
///
/// For any existing source and any world member who is neither the source's creator nor a GM,
/// attempting to update, delete, or mark-ready that source should be denied with a forbidden error.
///
/// **Validates: Requirements 1.8, 3.2, 4.2, 6.3**
/// </summary>
[TestFixture]
[Category("Feature: world-sources, Property 4: Only Creator or GM Can Mutate a Source")]
public class SourceMutationAuthorizationTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(MutationAuthorizationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: world-sources, Property 4: Only Creator or GM Can Mutate a Source")]
    public void NonCreatorPlayer_CannotUpdateSource(MutationScenario scenario)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, new InMemoryCampaignRepository(), queueClient);

        // Seed the source created by a different user
        sourceRepo.CreateAsync(scenario.ExistingSource, CancellationToken.None).GetAwaiter().GetResult();

        var command = new UpdateSourceCommand(
            scenario.ExistingSource.Id,
            scenario.ExistingSource.WorldId,
            scenario.ActingUserId,
            WorldRole.Player,
            Title: "Updated Title");

        // Act
        var result = service.UpdateAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should be denied with forbidden error
        Assert.That(result.IsSuccess, Is.False,
            "Non-creator Player should not be able to update a source.");
        Assert.That(result.Error!.Code, Is.EqualTo("forbidden"),
            "Error code should be 'forbidden' for unauthorized mutation.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403),
            "Status code should be 403 for unauthorized mutation.");

        // Assert — source should remain unchanged
        var unchanged = sourceRepo.Sources.First(s => s.Id == scenario.ExistingSource.Id);
        Assert.That(unchanged.Title, Is.EqualTo(scenario.ExistingSource.Title),
            "Source title should remain unchanged after failed update.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(MutationAuthorizationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: world-sources, Property 4: Only Creator or GM Can Mutate a Source")]
    public void NonCreatorPlayer_CannotDeleteSource(MutationScenario scenario)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, new InMemoryCampaignRepository(), queueClient);

        // Seed the source created by a different user
        sourceRepo.CreateAsync(scenario.ExistingSource, CancellationToken.None).GetAwaiter().GetResult();

        // Act
        var result = service.DeleteAsync(
            scenario.ExistingSource.Id,
            scenario.ExistingSource.WorldId,
            scenario.ActingUserId,
            WorldRole.Player,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should be denied with forbidden error
        Assert.That(result.IsSuccess, Is.False,
            "Non-creator Player should not be able to delete a source.");
        Assert.That(result.Error!.Code, Is.EqualTo("forbidden"),
            "Error code should be 'forbidden' for unauthorized deletion.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403),
            "Status code should be 403 for unauthorized deletion.");

        // Assert — source should still exist
        var stillExists = sourceRepo.Sources.Any(s => s.Id == scenario.ExistingSource.Id);
        Assert.That(stillExists, Is.True,
            "Source should still exist after failed delete attempt.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(MutationAuthorizationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: world-sources, Property 4: Only Creator or GM Can Mutate a Source")]
    public void NonCreatorPlayer_CannotMarkSourceReady(MutationScenario scenario)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, new InMemoryCampaignRepository(), queueClient);

        // Seed the source created by a different user (in Draft status for mark-ready)
        var draftSource = scenario.ExistingSource;
        draftSource.ProcessingStatus = SourceProcessingStatus.Draft;
        sourceRepo.CreateAsync(draftSource, CancellationToken.None).GetAwaiter().GetResult();

        var command = new MarkSourceReadyCommand(
            draftSource.Id,
            draftSource.WorldId,
            scenario.ActingUserId,
            WorldRole.Player);

        // Act
        var result = service.MarkReadyAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — should be denied with forbidden error
        Assert.That(result.IsSuccess, Is.False,
            "Non-creator Player should not be able to mark a source as ready.");
        Assert.That(result.Error!.Code, Is.EqualTo("forbidden"),
            "Error code should be 'forbidden' for unauthorized mark-ready.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403),
            "Status code should be 403 for unauthorized mark-ready.");

        // Assert — source should remain in Draft status
        var unchanged = sourceRepo.Sources.First(s => s.Id == draftSource.Id);
        Assert.That(unchanged.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Draft),
            "Source should remain in Draft status after failed mark-ready attempt.");
    }
}

/// <summary>
/// Input model for mutation authorization scenarios.
/// Represents a source owned by one user and an acting user (Player) who is NOT the creator.
/// </summary>
public record MutationScenario(
    Source ExistingSource,
    Guid ActingUserId);

/// <summary>
/// Custom FsCheck arbitraries for mutation authorization tests.
/// Generates scenarios where a Player attempts to mutate a source they did not create.
/// </summary>
public class MutationAuthorizationArbitraries
{
    private static readonly char[] TitleChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'".ToCharArray();

    public static Arbitrary<MutationScenario> MutationScenarios()
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

        var creatorRoleGen = Gen.Elements(WorldRole.GM, WorldRole.Player);

        var gen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from creatorUserId in ArbMap.Default.GeneratorFor<Guid>()
            from actingUserId in ArbMap.Default.GeneratorFor<Guid>()
            where actingUserId != creatorUserId // Ensure actor is different from creator
            from title in validTitleGen
            from sourceType in sourceTypeGen
            from visibility in visibilityGen
            from creatorRole in creatorRoleGen
            from daysAgo in Gen.Choose(0, 365)
            select new MutationScenario(
                new Source
                {
                    Id = Guid.NewGuid(),
                    WorldId = worldId,
                    Type = sourceType,
                    Title = title,
                    Body = "Session notes from Black Harbor investigation",
                    Uri = null,
                    OccurredAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
                    CreatedByUserId = creatorUserId,
                    Visibility = visibility,
                    ProcessingStatus = SourceProcessingStatus.Draft
                },
                actingUserId);

        return gen.ToArbitrary();
    }
}
