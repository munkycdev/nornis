using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 1: Source Creation Field Mapping
///
/// For any valid source creation input (Title 1–200 non-blank chars, valid SourceType, valid VisibilityScope,
/// optional Body ≤100,000 chars, optional Uri ≤2,048 chars, optional OccurredAt), creating a source should
/// return a Source with: the provided Title, Type, Visibility, Body, Uri, OccurredAt correctly stored;
/// ProcessingStatus set to Draft; CreatedByUserId matching the acting user; CreatedAt set to approximately
/// the current UTC time; and a non-empty Id.
///
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4**
/// </summary>
[TestFixture]
[Category("Feature: world-sources, Property 1: Source Creation Field Mapping")]
public class SourceCreationFieldMappingTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(SourceCreationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: world-sources, Property 1: Source Creation Field Mapping")]
    public void SourceCreation_MapsAllFieldsCorrectly(SourceCreationInput input)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, new InMemoryCampaignRepository(), queueClient);

        var command = new CreateSourceCommand(
            input.WorldId,
            input.Title,
            input.Type,
            input.Visibility,
            input.CreatingUserId,
            input.CreatingUserRole,
            input.Body,
            input.Uri,
            input.OccurredAt);

        var beforeCreation = DateTimeOffset.UtcNow;

        // Act
        var result = service.CreateAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        var afterCreation = DateTimeOffset.UtcNow;

        // Assert - operation should succeed
        Assert.That(result.IsSuccess, Is.True, "Source creation should succeed for valid input.");

        var source = result.Value!;

        // Assert - Title matches input
        Assert.That(source.Title, Is.EqualTo(input.Title),
            "Source Title must match the provided input.");

        // Assert - Type matches input
        Assert.That(source.Type, Is.EqualTo(input.Type),
            "Source Type must match the provided input.");

        // Assert - Visibility matches input
        Assert.That(source.Visibility, Is.EqualTo(input.Visibility),
            "Source Visibility must match the provided input.");

        // Assert - Body matches input
        Assert.That(source.Body, Is.EqualTo(input.Body),
            "Source Body must match the provided input.");

        // Assert - Uri matches input
        Assert.That(source.Uri, Is.EqualTo(input.Uri),
            "Source Uri must match the provided input.");

        // Assert - OccurredAt matches input
        Assert.That(source.OccurredAt, Is.EqualTo(input.OccurredAt),
            "Source OccurredAt must match the provided input.");

        // Assert - ProcessingStatus is Draft
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Draft),
            "Source ProcessingStatus must be Draft on creation.");

        // Assert - CreatedByUserId matches the acting user
        Assert.That(source.CreatedByUserId, Is.EqualTo(input.CreatingUserId),
            "Source CreatedByUserId must match the acting user's Id.");

        // Assert - CreatedAt is approximately the current UTC time
        Assert.That(source.CreatedAt, Is.GreaterThanOrEqualTo(beforeCreation),
            "Source CreatedAt must be at or after the time creation was initiated.");
        Assert.That(source.CreatedAt, Is.LessThanOrEqualTo(afterCreation),
            "Source CreatedAt must be at or before the time creation completed.");

        // Assert - Non-empty Id
        Assert.That(source.Id, Is.Not.EqualTo(Guid.Empty),
            "Source Id must be a non-empty Guid.");

        // Assert - WorldId matches input
        Assert.That(source.WorldId, Is.EqualTo(input.WorldId),
            "Source WorldId must match the provided input.");
    }
}

/// <summary>
/// Input model for source creation field mapping property tests.
/// </summary>
public record SourceCreationInput(
    Guid WorldId,
    string Title,
    SourceType Type,
    VisibilityScope Visibility,
    Guid CreatingUserId,
    WorldRole CreatingUserRole,
    string? Body,
    string? Uri,
    DateTimeOffset? OccurredAt);

/// <summary>
/// Custom FsCheck arbitraries for source creation field mapping tests.
/// </summary>
public class SourceCreationArbitraries
{
    private static readonly char[] TitleChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'.,:;!?".ToCharArray();

    private static readonly char[] BodyChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!?-\n\r\t".ToCharArray();

    private static readonly char[] UriChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%".ToCharArray();

    public static Arbitrary<SourceCreationInput> SourceCreationInputs()
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
            VisibilityScope.PartyVisible);

        // GM can set any visibility; Player cannot set GMOnly
        // To keep the generator simple and always valid, we generate GM or Player roles
        // and constrain visibility based on role
        var roleGen = Gen.Elements(WorldRole.GM, WorldRole.Player);

        var optionalBodyGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            from length in Gen.Choose(1, 500) // keep body short for test speed
            from chars in Gen.Elements(BodyChars).ArrayOf(length)
            select (string?)new string(chars));

        var optionalUriGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            from length in Gen.Choose(1, 200) // keep uri short for test speed
            from chars in Gen.Elements(UriChars).ArrayOf(length)
            select (string?)new string(chars));

        var optionalOccurredAtGen = Gen.OneOf(
            Gen.Constant<DateTimeOffset?>(null),
            from daysAgo in Gen.Choose(0, 365)
            select (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-daysAgo));

        var inputGen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from title in validTitleGen
            from sourceType in validSourceTypeGen
            from role in roleGen
            from visibility in role == WorldRole.GM
                ? Gen.Elements(VisibilityScope.Private, VisibilityScope.GMOnly, VisibilityScope.PartyVisible)
                : validVisibilityGen
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from body in optionalBodyGen
            from uri in optionalUriGen
            from occurredAt in optionalOccurredAtGen
            select new SourceCreationInput(worldId, title, sourceType, visibility, userId, role, body, uri, occurredAt);

        return inputGen.ToArbitrary();
    }
}
