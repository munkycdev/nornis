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
/// Property 3: Players Cannot Set GMOnly Visibility
///
/// For any source creation or update request where the acting user has role Player and the requested
/// VisibilityScope is GMOnly, the service should reject the request with a validation error without
/// modifying any stored data.
///
/// **Validates: Requirements 1.9, 3.6, 9.5**
/// </summary>
[TestFixture]
[Category("Feature: campaign-sources, Property 3: Players Cannot Set GMOnly Visibility")]
public class SourcePlayerCannotSetGMOnlyVisibilityTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(PlayerGMOnlyArbitraries)],
        MaxTest = 100)]
    [Description("Feature: campaign-sources, Property 3: Players Cannot Set GMOnly Visibility - CreateAsync rejects Player GMOnly")]
    public void CreateAsync_RejectsPlayerWithGMOnlyVisibility(PlayerGMOnlyCreateInput input)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, queueClient);

        var command = new CreateSourceCommand(
            input.CampaignId,
            input.Title,
            input.Type,
            VisibilityScope.GMOnly,
            input.UserId,
            CampaignRole.Player,
            input.Body,
            input.Uri,
            input.OccurredAt);

        // Act
        var result = service.CreateAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert - operation should fail with validation error
        Assert.That(result.IsSuccess, Is.False,
            "Source creation should be rejected when Player attempts GMOnly visibility.");
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"),
            "Error code should be validation_error.");
        Assert.That(result.Error.StatusCode, Is.EqualTo(400),
            "Validation error should return status code 400.");

        // Assert - no data was stored
        Assert.That(sourceRepo.Sources, Is.Empty,
            "No source should be stored when Player attempts GMOnly visibility.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(PlayerGMOnlyArbitraries)],
        MaxTest = 100)]
    [Description("Feature: campaign-sources, Property 3: Players Cannot Set GMOnly Visibility - UpdateAsync rejects Player GMOnly")]
    public void UpdateAsync_RejectsPlayerUpdatingToGMOnlyVisibility(PlayerGMOnlyUpdateInput input)
    {
        // Arrange - create a valid source owned by the Player
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, queueClient);

        var existingSource = new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = input.CampaignId,
            Type = input.Type,
            Title = input.Title,
            Body = input.Body,
            Uri = input.Uri,
            Visibility = input.OriginalVisibility,
            ProcessingStatus = SourceProcessingStatus.Draft,
            CreatedByUserId = input.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        sourceRepo.CreateAsync(existingSource, CancellationToken.None).GetAwaiter().GetResult();

        var originalVisibility = existingSource.Visibility;
        var originalTitle = existingSource.Title;

        // Act - attempt update with GMOnly visibility
        var updateCommand = new UpdateSourceCommand(
            existingSource.Id,
            input.CampaignId,
            input.UserId,
            CampaignRole.Player,
            Visibility: VisibilityScope.GMOnly);

        var result = service.UpdateAsync(updateCommand, CancellationToken.None).GetAwaiter().GetResult();

        // Assert - operation should fail with validation error
        Assert.That(result.IsSuccess, Is.False,
            "Source update should be rejected when Player attempts to set GMOnly visibility.");
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"),
            "Error code should be validation_error.");
        Assert.That(result.Error.StatusCode, Is.EqualTo(400),
            "Validation error should return status code 400.");

        // Assert - source was not modified
        var storedSource = sourceRepo.Sources.Single(s => s.Id == existingSource.Id);
        Assert.That(storedSource.Visibility, Is.EqualTo(originalVisibility),
            "Source visibility should not be modified when Player attempts GMOnly.");
        Assert.That(storedSource.Title, Is.EqualTo(originalTitle),
            "Source title should not be modified when update is rejected.");
    }
}

/// <summary>
/// Input model for Player GMOnly create scenario.
/// </summary>
public record PlayerGMOnlyCreateInput(
    Guid CampaignId,
    string Title,
    SourceType Type,
    Guid UserId,
    string? Body,
    string? Uri,
    DateTimeOffset? OccurredAt);

/// <summary>
/// Input model for Player GMOnly update scenario.
/// </summary>
public record PlayerGMOnlyUpdateInput(
    Guid CampaignId,
    string Title,
    SourceType Type,
    VisibilityScope OriginalVisibility,
    Guid UserId,
    string? Body,
    string? Uri);

/// <summary>
/// Custom FsCheck arbitraries for Player GMOnly visibility tests.
/// Generates random valid source properties with role=Player and visibility=GMOnly.
/// </summary>
public class PlayerGMOnlyArbitraries
{
    private static readonly char[] TitleChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'.,:;!?".ToCharArray();

    private static readonly char[] BodyChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!?-\n\r\t".ToCharArray();

    private static readonly char[] UriChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%".ToCharArray();

    public static Arbitrary<PlayerGMOnlyCreateInput> PlayerGMOnlyCreateInputs()
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

        var optionalBodyGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            from length in Gen.Choose(1, 500)
            from chars in Gen.Elements(BodyChars).ArrayOf(length)
            select (string?)new string(chars));

        var optionalUriGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            from length in Gen.Choose(1, 200)
            from chars in Gen.Elements(UriChars).ArrayOf(length)
            select (string?)new string(chars));

        var optionalOccurredAtGen = Gen.OneOf(
            Gen.Constant<DateTimeOffset?>(null),
            from daysAgo in Gen.Choose(0, 365)
            select (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-daysAgo));

        var inputGen =
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from title in validTitleGen
            from sourceType in sourceTypeGen
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from body in optionalBodyGen
            from uri in optionalUriGen
            from occurredAt in optionalOccurredAtGen
            select new PlayerGMOnlyCreateInput(campaignId, title, sourceType, userId, body, uri, occurredAt);

        return inputGen.ToArbitrary();
    }

    public static Arbitrary<PlayerGMOnlyUpdateInput> PlayerGMOnlyUpdateInputs()
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

        // Original visibility must be non-GMOnly (Private or PartyVisible) since
        // this is a Player's own source being updated to GMOnly
        var originalVisibilityGen = Gen.Elements(
            VisibilityScope.Private,
            VisibilityScope.PartyVisible);

        var optionalBodyGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            from length in Gen.Choose(1, 500)
            from chars in Gen.Elements(BodyChars).ArrayOf(length)
            select (string?)new string(chars));

        var optionalUriGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            from length in Gen.Choose(1, 200)
            from chars in Gen.Elements(UriChars).ArrayOf(length)
            select (string?)new string(chars));

        var inputGen =
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from title in validTitleGen
            from sourceType in sourceTypeGen
            from originalVisibility in originalVisibilityGen
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from body in optionalBodyGen
            from uri in optionalUriGen
            select new PlayerGMOnlyUpdateInput(campaignId, title, sourceType, originalVisibility, userId, body, uri);

        return inputGen.ToArbitrary();
    }
}
