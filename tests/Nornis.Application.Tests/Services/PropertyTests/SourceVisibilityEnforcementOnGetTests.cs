using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 5: Visibility Enforcement on Get
///
/// For any source in a world and any world member, retrieving that source by Id should return
/// the source details if and only if the member can see it according to visibility rules
/// (PartyVisible: all members; Private: creator or GM; GMOnly: GM only).
/// When visibility denies access, the response should be not-found (not forbidden).
///
/// **Validates: Requirements 2.2, 2.3, 2.4, 2.7, 9.1, 9.2, 9.3, 9.4**
/// </summary>
[TestFixture]
[Category("Feature: world-sources, Property 5: Visibility Enforcement on Get")]
public class SourceVisibilityEnforcementOnGetTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(VisibilityGetArbitraries)],
        MaxTest = 100)]
    [Description("Feature: world-sources, Property 5: Visibility Enforcement on Get")]
    public void GetById_ReturnsSource_IfAndOnlyIf_VisibilityAllows(VisibilityGetScenario scenario)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, new InMemoryCampaignRepository(), queueClient,
            new InMemoryReviewBatchRepository(), new InMemorySourceAttachmentRepository(),
            new FakeBlobStorageService(), NullLogger<SourceService>.Instance);

        // Seed the source
        sourceRepo.CreateAsync(scenario.ExistingSource, CancellationToken.None).GetAwaiter().GetResult();

        // Determine expected visibility using the same rules as the design
        var expectedCanSee = CanSee(
            scenario.ExistingSource.Visibility,
            scenario.RequestingRole,
            scenario.RequestingUserId,
            scenario.ExistingSource.CreatedByUserId);

        // Act
        var result = service.GetByIdAsync(
            scenario.ExistingSource.Id,
            scenario.ExistingSource.WorldId,
            scenario.RequestingUserId,
            scenario.RequestingRole,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        if (expectedCanSee)
        {
            Assert.That(result.IsSuccess, Is.True,
                $"Member with role {scenario.RequestingRole} should see a {scenario.ExistingSource.Visibility} source " +
                $"(isCreator={scenario.RequestingUserId == scenario.ExistingSource.CreatedByUserId}).");

            Assert.That(result.Value!.Id, Is.EqualTo(scenario.ExistingSource.Id),
                "Returned source Id should match the requested source.");
            Assert.That(result.Value!.Title, Is.EqualTo(scenario.ExistingSource.Title),
                "Returned source Title should match the stored source.");
            Assert.That(result.Value!.Visibility, Is.EqualTo(scenario.ExistingSource.Visibility),
                "Returned source Visibility should match the stored source.");
        }
        else
        {
            Assert.That(result.IsSuccess, Is.False,
                $"Member with role {scenario.RequestingRole} should NOT see a {scenario.ExistingSource.Visibility} source " +
                $"(isCreator={scenario.RequestingUserId == scenario.ExistingSource.CreatedByUserId}).");

            Assert.That(result.Error!.Code, Is.EqualTo("not_found"),
                "Visibility denial should return 'not_found' error code (not forbidden).");
            Assert.That(result.Error!.StatusCode, Is.EqualTo(404),
                "Visibility denial should return 404 status (not 403).");
        }
    }

    /// <summary>
    /// Expected visibility decision function matching the design specification.
    /// </summary>
    private static bool CanSee(VisibilityScope visibility, WorldRole role, Guid requestingUserId, Guid creatorUserId) =>
        visibility switch
        {
            VisibilityScope.PartyVisible => true,
            VisibilityScope.Private => role == WorldRole.GM || requestingUserId == creatorUserId,
            VisibilityScope.GMOnly => role == WorldRole.GM,
            _ => false
        };
}

/// <summary>
/// Input model for visibility enforcement on get scenarios.
/// Represents a source with a specific visibility and a requesting user with a specific role.
/// </summary>
public record VisibilityGetScenario(
    Source ExistingSource,
    Guid RequestingUserId,
    WorldRole RequestingRole);

/// <summary>
/// Custom FsCheck arbitraries for visibility enforcement on get tests.
/// Generates all combinations of (visibility, role, isCreator) to test all visibility decision branches.
/// </summary>
public class VisibilityGetArbitraries
{
    private static readonly char[] TitleChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'".ToCharArray();

    public static Arbitrary<VisibilityGetScenario> VisibilityGetScenarios()
    {
        var validTitleGen =
            from length in Gen.Choose(1, 80)
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

        var roleGen = Gen.Elements(
            WorldRole.GM,
            WorldRole.Player,
            WorldRole.Observer);

        // Boolean to determine if the requester is the creator
        var isCreatorGen = Gen.Elements(true, false);

        var gen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from creatorUserId in ArbMap.Default.GeneratorFor<Guid>()
            from otherUserId in ArbMap.Default.GeneratorFor<Guid>()
            where otherUserId != creatorUserId
            from isCreator in isCreatorGen
            let requestingUserId = isCreator ? creatorUserId : otherUserId
            from role in roleGen
            from visibility in visibilityGen
            from title in validTitleGen
            from sourceType in sourceTypeGen
            from daysAgo in Gen.Choose(0, 365)
            select new VisibilityGetScenario(
                new Source
                {
                    Id = Guid.NewGuid(),
                    WorldId = worldId,
                    Type = sourceType,
                    Title = title,
                    Body = "Captain Voss was questioned in Black Harbor.",
                    Uri = null,
                    OccurredAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
                    CreatedByUserId = creatorUserId,
                    Visibility = visibility,
                    ProcessingStatus = SourceProcessingStatus.Draft
                },
                requestingUserId,
                role);

        return gen.ToArbitrary();
    }
}
